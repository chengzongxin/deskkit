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
    var starterSeeded = false
    var hiddenTools: [String] = []
    var toolOrder: [String] = []
    var summonKeyCode = 49
    var summonAlt = true
    var summonCommand = false
    var summonControl = false
    var summonShift = false
    var lastScreen = ""
    var panelPlacedByUser = false
    var panelX = 0.0
    var panelY = 0.0
    var panelWidth = 0.0
    var panelHeight = 0.0
    var dismissedClip = ""
    var hotkeys: [String: HotkeyBinding] = [:]
    var triggerWords: [String: String] = [:]

    private enum CodingKeys: String, CodingKey {
        case schemaVersion, autoLaunch, summonMode, blurHide, multiScreen, theme, density, accentToken
        case backgroundImagePath, backgroundOpacity
        case clipboardRetention, clipboardPaused, cloudSync, autoUpdate, guideDone, starterSeeded, hiddenTools, toolOrder
        case summonKeyCode, summonAlt, summonCommand, summonControl, summonShift, lastScreen, dismissedClip
        case panelPlacedByUser, panelX, panelY, panelWidth, panelHeight
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
        starterSeeded = try c.decodeIfPresent(Bool.self, forKey: .starterSeeded) ?? false
        hiddenTools = try c.decodeIfPresent([String].self, forKey: .hiddenTools) ?? []
        toolOrder = try c.decodeIfPresent([String].self, forKey: .toolOrder) ?? []
        summonKeyCode = try c.decodeIfPresent(Int.self, forKey: .summonKeyCode) ?? 49
        summonAlt = try c.decodeIfPresent(Bool.self, forKey: .summonAlt) ?? true
        summonCommand = try c.decodeIfPresent(Bool.self, forKey: .summonCommand) ?? false
        summonControl = try c.decodeIfPresent(Bool.self, forKey: .summonControl) ?? false
        summonShift = try c.decodeIfPresent(Bool.self, forKey: .summonShift) ?? false
        lastScreen = try c.decodeIfPresent(String.self, forKey: .lastScreen) ?? ""
        panelPlacedByUser = try c.decodeIfPresent(Bool.self, forKey: .panelPlacedByUser) ?? false
        panelX = try c.decodeIfPresent(Double.self, forKey: .panelX) ?? 0
        panelY = try c.decodeIfPresent(Double.self, forKey: .panelY) ?? 0
        panelWidth = try c.decodeIfPresent(Double.self, forKey: .panelWidth) ?? 0
        panelHeight = try c.decodeIfPresent(Double.self, forKey: .panelHeight) ?? 0
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
        if purgeReservedNavGroup() { write(library, name: "groups.json") }
        seedStarterIfNeeded()
    }

    private func seedStarterIfNeeded() {
        guard !settings.starterSeeded else { return }
        if library.items.isEmpty {
            let added = applyStarter(merge: false)
            Log.info("初始快捷启动已放入 \(added) 项")
        }
        settings.starterSeeded = true
        save()
    }

    @discardableResult
    func importStarterTools() -> Int {
        let added = applyStarter(merge: true)
        settings.starterSeeded = true
        save()
        onChange()
        Log.info(added == 0 ? "系统工具和已安装应用都已经在快捷启动里" : "已放入 \(added) 个快捷项")
        return added
    }

    private func applyStarter(merge: Bool) -> Int {
        let catalog = store.starter()
        let curated = StarterLocator.find(catalog.items)
        let taken = curated.map(\.path) + library.items.map(\.path)
        let hits = curated + StarterLocator.installed(taken: taken)
        let built = StarterPack.compose(groups: catalog.groups, found: hits, ungrouped: catalog.ungrouped)
        if !merge {
            library.schemaVersion = 1
            library.groups = built.groups.map { Group(id: $0.id, name: $0.name, color: $0.color, itemIds: $0.itemIds) }
            library.items = built.items.map { Shortcut(id: $0.id, title: $0.title, path: $0.path, kind: $0.kind) }
            return library.items.count
        }

        for group in built.groups where group.id != catalog.ungrouped.id && !library.groups.contains(where: { $0.id == group.id }) {
            let created = Group(id: group.id, name: group.name, color: group.color, itemIds: [])
            if let ungrouped = library.groups.firstIndex(where: { $0.id == catalog.ungrouped.id }) {
                library.groups.insert(created, at: ungrouped)
            } else {
                library.groups.append(created)
            }
        }

        var known = Set(library.items.map { $0.path.lowercased() })
        var added = 0
        for item in built.items {
            let key = item.path.lowercased()
            if known.contains(key) { continue }
            guard let group = built.groups.first(where: { $0.itemIds.contains(item.id) }) else { continue }
            let id = library.items.contains(where: { $0.id == item.id }) ? UUID().uuidString : item.id
            library.items.append(Shortcut(id: id, title: item.title, path: item.path, kind: item.kind))
            if let index = library.groups.firstIndex(where: { $0.id == group.id }) {
                library.groups[index].itemIds.append(id)
            }
            known.insert(key)
            added += 1
        }
        return added
    }

    @discardableResult
    private func purgeReservedNavGroup() -> Bool {
        guard let index = library.groups.firstIndex(where: { $0.id == "fav" }) else { return false }
        let orphaned = library.groups[index].itemIds
        library.groups.remove(at: index)
        guard !orphaned.isEmpty else { return true }
        if let ungrouped = library.groups.firstIndex(where: { $0.id == "ungrouped" }) {
            for itemId in orphaned where !library.groups[ungrouped].itemIds.contains(itemId) {
                library.groups[ungrouped].itemIds.append(itemId)
            }
        } else {
            library.groups.append(Group(id: "ungrouped", name: "未分组", color: "#98A1B0", itemIds: orphaned))
        }
        return true
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

    func reorderGroup(_ sourceId: String, relativeTo targetId: String, placeAfter: Bool = false) {
        guard sourceId != targetId else { return }
        guard let from = library.groups.firstIndex(where: { $0.id == sourceId }),
              let to = library.groups.firstIndex(where: { $0.id == targetId }) else { return }
        let group = library.groups.remove(at: from)
        var insert = to
        if from < to { insert -= 1 }
        if placeAfter { insert += 1 }
        library.groups.insert(group, at: insert)
        save()
        Log.info("分组顺序已更新")
        onChange()
    }

    func moveShortcut(_ itemId: String, from sourceGroupId: String, to targetGroupId: String) {
        guard sourceGroupId != targetGroupId else { return }
        guard let source = library.groups.firstIndex(where: { $0.id == sourceGroupId }),
              let target = library.groups.firstIndex(where: { $0.id == targetGroupId }) else { return }
        guard library.groups[source].itemIds.contains(itemId) else { return }
        library.groups[source].itemIds.removeAll { $0 == itemId }
        if !library.groups[target].itemIds.contains(itemId) {
            library.groups[target].itemIds.append(itemId)
        }
        save()
        let name = library.items.first { $0.id == itemId }?.title ?? itemId
        Log.info("已将「\(name)」从「\(library.groups[source].name)」移到「\(library.groups[target].name)」")
        onChange()
    }

    func reorderShortcut(_ itemId: String, in groupId: String, before beforeId: String, placeAfter: Bool) {
        guard itemId != beforeId,
              let index = library.groups.firstIndex(where: { $0.id == groupId }) else { return }
        var ids = library.groups[index].itemIds
        guard let from = ids.firstIndex(of: itemId) else { return }
        ids.remove(at: from)
        guard let anchor = ids.firstIndex(of: beforeId) else { return }
        let insert = placeAfter ? anchor + 1 : anchor
        ids.insert(itemId, at: min(insert, ids.count))
        library.groups[index].itemIds = ids
        save()
        Log.info("快捷项已在分组内调整顺序")
        onChange()
    }

    func renameGroup(_ id: String, to name: String) {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, let index = library.groups.firstIndex(where: { $0.id == id }) else {
            Log.warn("分组名称不能为空")
            return
        }
        guard library.groups[index].name != trimmed else { return }
        library.groups[index].name = trimmed
        save()
        Log.info("分组已重命名为「\(trimmed)」")
        onChange()
    }

    func deleteGroup(_ id: String) {
        guard id != "ungrouped" else {
            Log.warn("不能删除未分组")
            return
        }
        guard let group = library.groups.first(where: { $0.id == id }) else {
            Log.warn("分组不存在")
            return
        }
        var records = library.groups.map { GroupRecord(id: $0.id, name: $0.name, color: $0.color, itemIds: $0.itemIds) }
        var items = library.items.map { ItemRecord(id: $0.id, title: $0.title) }
        let removed = GroupEditor.delete(groups: &records, items: &items, groupId: id, ungroupedId: "ungrouped", ungroupedName: "未分组")
        guard removed.ok else {
            Log.warn(removed.message)
            return
        }
        let remaining = Set(items.map(\.id))
        library.groups = records.map { record in
            Group(id: record.id, name: record.name, color: record.color.isEmpty ? "#98A1B0" : record.color, itemIds: record.itemIds)
        }
        library.items.removeAll { !remaining.contains($0.id) }
        save()
        Log.info("已删除分组「\(group.name)」，并移除 \(group.itemIds.count) 个快捷项")
        onChange()
    }

    @discardableResult
    func addShortcut(url: URL, groupId: String) -> Bool {
        let target = resolveGroup(groupId)
        let stored: String
        let kind: String
        let title: String
        let label: String
        if url.isFileURL {
            let file = url.standardizedFileURL
            stored = file.path
            var directory: ObjCBool = false
            guard FileManager.default.fileExists(atPath: stored, isDirectory: &directory) else {
                Log.warn("快捷项路径不存在 \(stored)")
                return false
            }
            if file.pathExtension.lowercased() == "app" {
                kind = "app"
                label = "应用"
                title = file.deletingPathExtension().lastPathComponent
            } else if directory.boolValue {
                kind = "file"
                label = "文件夹"
                title = file.lastPathComponent
            } else {
                kind = "file"
                label = "文件"
                title = file.lastPathComponent
            }
        } else if let scheme = url.scheme?.lowercased(), scheme == "http" || scheme == "https" {
            stored = url.absoluteString
            kind = "url"
            label = "网址"
            title = url.host ?? url.absoluteString
        } else {
            Log.warn("不能放入快捷启动 \(url.absoluteString)")
            return false
        }
        let name = title.isEmpty ? stored : title
        if let existing = library.items.first(where: { $0.path == stored }) {
            guard let index = library.groups.firstIndex(where: { $0.id == target }) else { return false }
            if library.groups[index].itemIds.contains(existing.id) {
                Log.info("快捷项已在这个分组里，跳过 \(existing.title)")
                return false
            }
            library.groups[index].itemIds.append(existing.id)
            save()
            onChange()
            Log.info("快捷项已放入「\(library.groups[index].name)」\(existing.title)")
            return true
        }
        let id = UUID().uuidString
        library.items.append(Shortcut(id: id, title: name, path: stored, kind: kind))
        if let index = library.groups.firstIndex(where: { $0.id == target }) {
            library.groups[index].itemIds.append(id)
        }
        save()
        onChange()
        let whereName = library.groups.first { $0.id == target }?.name ?? target
        Log.info("已添加快捷项 \(name)（\(label)）到「\(whereName)」")
        return true
    }

    private func resolveGroup(_ groupId: String) -> String {
        if library.groups.contains(where: { $0.id == groupId }) { return groupId }
        if library.groups.contains(where: { $0.id == "ungrouped" }) { return "ungrouped" }
        if let first = library.groups.first?.id { return first }
        library.groups.append(Group(id: "ungrouped", name: "未分组", color: "#98A1B0", itemIds: []))
        return "ungrouped"
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
