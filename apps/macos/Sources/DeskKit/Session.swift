import AppKit
import DeskKitCore
import Foundation

struct Clip: Codable {
    var id: String
    var kind: String
    var text: String
    var createdAt: String
    var favorite: Bool
}

struct Shortcut: Codable {
    var id: String
    var title: String
    var path: String
    var kind: String
}

struct Group: Codable {
    var id: String
    var name: String
    var color: String
    var itemIds: [String]
}

struct Library: Codable {
    var schemaVersion: Int = 1
    var groups: [Group]
    var items: [Shortcut]
}

struct HotkeyBinding: Codable, Equatable {
    var keyCode: Int
    var alt: Bool
    var command: Bool
    var control: Bool
    var shift: Bool
    var text: String
}

struct Settings: Codable {
    var schemaVersion: Int = 1
    var autoLaunch = false
    var summonMode = "hotkey"
    var blurHide = true
    var multiScreen = "followCursor"
    var theme = "light"
    var density = "standard"
    var accentToken = "accent"
    var backgroundImagePath = ""
    var backgroundOpacity = 1.0
    var clipboardRetention = "7d"
    var clipboardPaused = false
    var cloudSync = false
    var autoUpdate = true
    var guideDone = false
    var hiddenTools: [String] = []
    var toolOrder: [String] = []
    var summonKeyCode = 49
    var summonAlt = true
    var summonCommand = false
    var summonControl = false
    var summonShift = false
    var lastScreen = ""
    var dismissedClip = ""
    var hotkeys: [String: HotkeyBinding] = [:]
    var triggerWords: [String: String] = [:]

    private enum CodingKeys: String, CodingKey {
        case schemaVersion, autoLaunch, summonMode, blurHide, multiScreen, theme, density, accentToken
        case backgroundImagePath, backgroundOpacity
        case clipboardRetention, clipboardPaused, cloudSync, autoUpdate, guideDone, hiddenTools, toolOrder
        case summonKeyCode, summonAlt, summonCommand, summonControl, summonShift, lastScreen, dismissedClip
        case hotkeys, triggerWords
    }

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try c.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 1
        autoLaunch = try c.decodeIfPresent(Bool.self, forKey: .autoLaunch) ?? false
        summonMode = try c.decodeIfPresent(String.self, forKey: .summonMode) ?? "hotkey"
        blurHide = try c.decodeIfPresent(Bool.self, forKey: .blurHide) ?? true
        multiScreen = try c.decodeIfPresent(String.self, forKey: .multiScreen) ?? "followCursor"
        theme = try c.decodeIfPresent(String.self, forKey: .theme) ?? "light"
        density = try c.decodeIfPresent(String.self, forKey: .density) ?? "standard"
        accentToken = try c.decodeIfPresent(String.self, forKey: .accentToken) ?? "accent"
        backgroundImagePath = try c.decodeIfPresent(String.self, forKey: .backgroundImagePath) ?? ""
        backgroundOpacity = min(1, max(0.15, try c.decodeIfPresent(Double.self, forKey: .backgroundOpacity) ?? 1))
        clipboardRetention = try c.decodeIfPresent(String.self, forKey: .clipboardRetention) ?? "7d"
        clipboardPaused = try c.decodeIfPresent(Bool.self, forKey: .clipboardPaused) ?? false
        cloudSync = try c.decodeIfPresent(Bool.self, forKey: .cloudSync) ?? false
        autoUpdate = try c.decodeIfPresent(Bool.self, forKey: .autoUpdate) ?? true
        guideDone = try c.decodeIfPresent(Bool.self, forKey: .guideDone) ?? false
        hiddenTools = try c.decodeIfPresent([String].self, forKey: .hiddenTools) ?? []
        toolOrder = try c.decodeIfPresent([String].self, forKey: .toolOrder) ?? []
        summonKeyCode = try c.decodeIfPresent(Int.self, forKey: .summonKeyCode) ?? 49
        summonAlt = try c.decodeIfPresent(Bool.self, forKey: .summonAlt) ?? true
        summonCommand = try c.decodeIfPresent(Bool.self, forKey: .summonCommand) ?? false
        summonControl = try c.decodeIfPresent(Bool.self, forKey: .summonControl) ?? false
        summonShift = try c.decodeIfPresent(Bool.self, forKey: .summonShift) ?? false
        lastScreen = try c.decodeIfPresent(String.self, forKey: .lastScreen) ?? ""
        dismissedClip = try c.decodeIfPresent(String.self, forKey: .dismissedClip) ?? ""
        hotkeys = try c.decodeIfPresent([String: HotkeyBinding].self, forKey: .hotkeys) ?? [:]
        triggerWords = try c.decodeIfPresent([String: String].self, forKey: .triggerWords) ?? [:]
    }
}

final class Session {
    let store: ContractStore
    let machine: LauncherStateMachine
    var settings = Settings()
    var library = Library(groups: [Group(id: "ungrouped", name: "未分组", color: "#98A1B0", itemIds: [])], items: [])
    var clips: [Clip] = []
    var index: [Hit] = []
    var recents: [String] = []
    var query = ""
    var triggerWord = ""
    var payload = ""
    var hits: [Hit] = []
    var selection = 0
    var onChange: () -> Void = {}

    init() {
        let root = ContractStore.locate()
            ?? Bundle.main.resourceURL?.appendingPathComponent("contracts")
            ?? URL(fileURLWithPath: FileManager.default.currentDirectoryPath).appendingPathComponent("../../contracts")
        store = try! ContractStore(root: root)
        machine = LauncherStateMachine(store: store) { Log.info($0) }
        load()
        Log.info("契约已加载，哈希 \(store.hash)")
    }

    func load() {
        settings = read(Settings.self, name: "settings.json") ?? Settings()
        if settings.toolOrder.isEmpty { settings.toolOrder = Catalog.tools.map(\.id) + ["add"] }
        library = read(Library.self, name: "groups.json") ?? library
        clips = read([Clip].self, name: "clipboard.json") ?? []
        recents = read([String].self, name: "recents.json") ?? []
    }

    func save() {
        write(settings, name: "settings.json")
        write(library, name: "groups.json")
        write(clips, name: "clipboard.json")
        write(recents, name: "recents.json")
    }

    func noteClipboard() {
        guard !settings.clipboardPaused else { return }
        let board = NSPasteboard.general
        let text = board.string(forType: .string) ?? ""
        let files = board.readObjects(forClasses: [NSURL.self]) as? [URL] ?? []
        let body: String
        let kind: String
        if !text.isEmpty {
            body = text
            kind = "text"
        } else if !files.isEmpty {
            body = files.map(\.path).joined(separator: "\n")
            kind = "file"
        } else {
            return
        }
        if clips.first?.text == body { return }
        let stamp = ISO8601DateFormatter().string(from: Date())
        clips.insert(Clip(id: UUID().uuidString, kind: kind, text: body, createdAt: stamp, favorite: false), at: 0)
        if clips.count > 200 { clips.removeLast(clips.count - 200) }
        save()
        pruneClips()
        Log.info("记录剪贴板 \(kind)，长度 \(body.count)")
        onChange()
    }

    func search() {
        let raw = query
        if let parsed = parseTrigger(raw) {
            triggerWord = parsed.word
            payload = parsed.payload
            if machine.state != "TOOL" { go("TOOL") }
            hits = toolModeHits(parsed.toolId, parsed.payload)
            selection = 0
            return
        }
        triggerWord = ""
        payload = ""
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty {
            hits = []
            if machine.state != "HIDDEN" { go("A1") }
            return
        }
        var found: [Hit] = []
        if looksLikeExpression(trimmed), ExpressionEvaluator.evaluate(trimmed).ok {
            let value = ExpressionEvaluator.evaluate(trimmed).data
            found.append(Hit(id: "calc", kind: "tool", title: "= \(value)", subtitle: "计算器", path: "", keywords: [], toolId: "calc", copyText: value, score: 100))
        }
        if let unit = UnitConverter.convert(trimmed) {
            found.append(Hit(id: "unit", kind: "tool", title: unit, subtitle: "单位换算", path: "", keywords: [], toolId: "unit", copyText: unit, score: 95))
        }
        if trimmed.hasPrefix("#") || (trimmed.count == 6 && trimmed.allSatisfy({ $0.isHexDigit })), let color = ColorConvert.describe(trimmed) {
            found.append(Hit(id: "color", kind: "tool", title: color, subtitle: "颜色转换", path: "", keywords: [], toolId: "color", copyText: color, score: 90))
        }
        if trimmed.allSatisfy(\.isNumber), trimmed.count == 10 || trimmed.count == 13, let time = TimestampConvert.describe(trimmed) {
            found.append(Hit(id: "ts", kind: "tool", title: time, subtitle: "时间戳", path: "", keywords: [], toolId: "timestamp", copyText: time, score: 90))
        }
        let rows = index.map { IndexRow(id: $0.id, kind: $0.kind, title: $0.title, keywords: $0.keywords) }
        let ids = Set(KeywordMatcher.match(rows, query: trimmed))
        let matched = index.filter { ids.contains($0.id) }.sorted { $0.score > $1.score }
        for kind in ["tool", "app", "file", "url"] {
            found.append(contentsOf: matched.filter { $0.kind == kind })
        }
        hits = found
        Log.info("搜索「\(trimmed)」命中 \(hits.count) 条，不截断")
        selection = 0
        go(hits.isEmpty ? "A4" : "A3")
    }

    func recentHits() -> [Hit] {
        recents.compactMap { id in index.first { $0.id == id } }.prefix(12).map { $0 }
    }

    func execute(hit: Hit) {
        if let copy = hit.copyText {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(copy, forType: .string)
            Log.info("已复制结果")
        } else if hit.kind == "tool", let toolId = hit.toolId {
            Log.info("打开工具 \(toolId)")
            onOpenTool?(toolId)
            return
        } else if !hit.path.isEmpty {
            NSWorkspace.shared.open(URL(fileURLWithPath: hit.path))
            Log.info("已打开 \(hit.title)")
        }
        remember(hit.id)
        hide()
    }

    func executeSelection() {
        guard hits.indices.contains(selection) else {
            if let first = hits.first { execute(hit: first) }
            return
        }
        execute(hit: hits[selection])
    }

    func escape() {
        _ = machine.escape()
        if machine.state == "HIDDEN" { onHide?() }
        else { onChange() }
    }

    func summon() {
        if machine.state == "HIDDEN" {
            shownAt = Date()
            _ = machine.transition("A1")
            query = ""
            triggerWord = ""
            hits = []
            Log.info("呼出浮层")
            onShow?()
        } else if Date().timeIntervalSince(shownAt) < 0.45 {
            Log.info("忽略刚呼出后的重复热键")
        } else {
            hide()
        }
    }

    func hide() {
        if machine.state != "HIDDEN" {
            _ = machine.transition("HIDDEN")
        }
        Log.info("隐藏浮层")
        onHide?()
    }

    func deleteGroup(_ id: String) {
        var records = library.groups.map { GroupRecord(id: $0.id, name: $0.name, color: $0.color, itemIds: $0.itemIds) }
        let items = library.items.map { ItemRecord(id: $0.id, title: $0.title) }
        _ = GroupEditor.delete(groups: &records, items: items, groupId: id, ungroupedId: "ungrouped", ungroupedName: "未分组")
        library.groups = records.map { record in
            Group(id: record.id, name: record.name, color: record.color.isEmpty ? "#98A1B0" : record.color, itemIds: record.itemIds)
        }
        save()
        Log.info("已删除分组，项目移入未分组")
        onChange()
    }

    func addShortcut(url: URL, groupId: String) {
        let id = UUID().uuidString
        let kind = url.pathExtension == "app" ? "app" : "file"
        library.items.append(Shortcut(id: id, title: url.deletingPathExtension().lastPathComponent, path: url.path, kind: kind))
        if let index = library.groups.firstIndex(where: { $0.id == groupId }) {
            library.groups[index].itemIds.append(id)
        }
        save()
        onChange()
    }

    var onShow: (() -> Void)?
    var onHide: (() -> Void)?
    var onOpenTool: ((String) -> Void)?
    private var shownAt = Date.distantPast

    private func hideAfterLaunch() { hide() }

    private func remember(_ id: String) {
        recents.removeAll { $0 == id }
        recents.insert(id, at: 0)
        if recents.count > 20 { recents.removeLast(recents.count - 20) }
        save()
    }

    private func go(_ next: String) {
        if machine.state == next { return }
        if machine.transition(next).ok { return }
        if machine.state != "HIDDEN" { _ = machine.transition("HIDDEN") }
        _ = machine.transition("A1")
        if next != "A1" { _ = machine.transition(next) }
    }

    func resolveTrigger(_ word: String) -> (toolId: String, label: String, placeholder: String)? {
        if let toolId = settings.triggerWords.first(where: { $0.value.caseInsensitiveCompare(word) == .orderedSame })?.key,
           let built = store.triggers.first(where: { $0.toolId == toolId }) {
            return (built.toolId, built.label, built.placeholder)
        }
        guard let built = store.trigger(word: word) else { return nil }
        if let custom = settings.triggerWords[built.toolId], custom.caseInsensitiveCompare(word) != .orderedSame {
            return nil
        }
        return built
    }

    func pruneClips() {
        let days: Double?
        switch settings.clipboardRetention {
        case "30d": days = 30
        case "forever": days = nil
        default: days = 7
        }
        guard let days else { return }
        let cutoff = Date().addingTimeInterval(-days * 86400)
        let before = clips.count
        clips.removeAll { clip in
            let date = ISO8601DateFormatter().date(from: clip.createdAt) ?? Date()
            return date < cutoff
        }
        if clips.count != before {
            save()
            Log.info("按保留策略清理剪贴板，剩余 \(clips.count) 条")
        }
    }

    private func parseTrigger(_ raw: String) -> (word: String, payload: String, toolId: String)? {
        guard let space = raw.firstIndex(of: " ") else { return nil }
        let word = String(raw[..<space])
        guard let found = resolveTrigger(word) else { return nil }
        let rest = String(raw[raw.index(after: space)...])
        return (word, rest, found.toolId)
    }

    private func toolModeHits(_ toolId: String, _ payload: String) -> [Hit] {
        switch toolId {
        case "translate":
            let result = Translator.translate(payload)
            return [Hit(id: "tr", kind: "tool", title: result.text, subtitle: store.text("translate.offline"), path: "", keywords: [], toolId: "translate", copyText: result.text, score: 100)]
        case "calc":
            if let unit = UnitConverter.convert(payload) {
                return [Hit(id: "unit", kind: "tool", title: unit, subtitle: "单位换算", path: "", keywords: [], toolId: "unit", copyText: unit, score: 100)]
            }
            let value = ExpressionEvaluator.evaluate(payload)
            let title = value.ok ? "= \(value.data)" : value.message
            return [Hit(id: "calc", kind: "tool", title: title, subtitle: "计算器", path: "", keywords: [], toolId: "calc", copyText: value.ok ? value.data : nil, score: 100)]
        case "color":
            let text = ColorConvert.describe(payload) ?? "无法识别颜色"
            return [Hit(id: "color", kind: "tool", title: text, subtitle: "颜色转换", path: "", keywords: [], toolId: "color", copyText: text, score: 100)]
        case "timestamp":
            let text = TimestampConvert.describe(payload) ?? "无法识别时间"
            return [Hit(id: "ts", kind: "tool", title: text, subtitle: "时间戳", path: "", keywords: [], toolId: "timestamp", copyText: text, score: 100)]
        case "clipboard":
            let needle = payload.lowercased()
            return clips.filter { needle.isEmpty || $0.text.lowercased().contains(needle) }.prefix(8).map {
                Hit(id: $0.id, kind: "tool", title: $0.text, subtitle: "剪贴板", path: "", keywords: [], toolId: "clipboard", copyText: $0.text, score: 80)
            }
        default:
            return []
        }
    }

    private func looksLikeExpression(_ text: String) -> Bool {
        text.contains { "+-*/()".contains($0) } && text.contains { $0.isNumber }
    }

    private func read<T: Decodable>(_ type: T.Type, name: String) -> T? {
        let url = Paths.file(name)
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(type, from: data)
    }

    private func write<T: Encodable>(_ value: T, name: String) {
        let url = Paths.file(name)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(value) else { return }
        let temporary = url.appendingPathExtension("tmp")
        try? data.write(to: temporary)
        if FileManager.default.fileExists(atPath: url.path) {
            _ = try? FileManager.default.replaceItemAt(url, withItemAt: temporary)
        } else {
            try? FileManager.default.moveItem(at: temporary, to: url)
        }
    }
}
