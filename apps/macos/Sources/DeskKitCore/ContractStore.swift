import CryptoKit
import Foundation

public struct DkResult {
    public var ok: Bool
    public var code: String
    public var message: String
    public var data: String

    public static func success(_ data: String = "") -> DkResult {
        DkResult(ok: true, code: "OK", message: "", data: data)
    }

    public static func fail(_ code: String, _ message: String) -> DkResult {
        DkResult(ok: false, code: code, message: message, data: "")
    }
}

public struct TriggerDef {
    public var word: String
    public var toolId: String
    public var label: String
    public var placeholder: String
}

public struct PermissionDef {
    public var id: String
    public var title: String
    public var reason: String
}

public struct StagedDiff {
    public var id: String
    public var category: String
    public var owner: String
    public var dueAt: String
    public var parityPlan: String
}

public final class ContractStore {
    public let root: URL
    public let hash: String
    private let files: [String: JSON]

    public init(root: URL) throws {
        self.root = root
        var loaded: [String: JSON] = [:]
        for relative in ContractStore.required {
            let url = root.appendingPathComponent(relative)
            guard FileManager.default.fileExists(atPath: url.path) else {
                throw ContractError("缺少契约文件 \(relative)")
            }
            loaded[relative] = try JSON.load(url: url)
        }
        guard loaded["contract-version.json"]?["contractVersion"]?.string == "1" else {
            throw ContractError("contractVersion 必须是 1")
        }
        for item in loaded["platform-diffs.json"]?["staged"]?.values ?? [] {
            for field in ["owner", "dueAt", "parityPlan"] where (item[field]?.string ?? "").isEmpty {
                throw ContractError("阶段性差异缺少 \(field)")
            }
        }
        files = loaded
        hash = try ContractStore.hashTree(root)
    }

    public static func locate(from start: URL = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)) -> URL? {
        if let env = ProcessInfo.processInfo.environment["DESKKIT_CONTRACTS"], !env.isEmpty {
            return URL(fileURLWithPath: env)
        }
        var seeds: [URL] = []
        if let resource = Bundle.main.resourceURL {
            seeds.append(resource.appendingPathComponent("contracts"))
        }
        seeds.append(start)
        if let executable = Bundle.main.executableURL {
            seeds.append(executable.deletingLastPathComponent())
        }
        for seed in seeds {
            var cursor = seed
            for _ in 0..<12 {
                if FileManager.default.fileExists(atPath: cursor.appendingPathComponent("contract-version.json").path) {
                    return cursor
                }
                let nested = cursor.appendingPathComponent("contracts/contract-version.json")
                if FileManager.default.fileExists(atPath: nested.path) {
                    return cursor.appendingPathComponent("contracts")
                }
                if cursor.path == "/" { break }
                cursor.deleteLastPathComponent()
            }
        }
        return nil
    }

    public func text(_ key: String) -> String {
        files["i18n.json"]?["strings"]?[key]?.string ?? key
    }

    public func text(_ key: String, _ values: [String: String]) -> String {
        var out = text(key)
        for (name, value) in values {
            out = out.replacingOccurrences(of: "{\(name)}", with: value)
        }
        return out
    }

    public func color(_ name: String) -> String {
        color(name, dark: false)
    }

    public func color(_ name: String, dark: Bool) -> String {
        if dark, let value = files["tokens.json"]?["dark"]?[name]?.string, !value.isEmpty {
            return value
        }
        return files["tokens.json"]?["color"]?[name]?.string ?? "#000000"
    }

    public var groupColors: [String] {
        files["tokens.json"]?["chip.group"]?.strings() ?? []
    }

    public func layout(_ path: String...) -> Double {
        var node = files["layout.json"]
        for key in path {
            node = node?[key]
        }
        return node?.double ?? 0
    }

    public func keyLabel(platform: String, key: String) -> String {
        files["keymap.json"]?["keys"]?[platform]?[key]?.string ?? key.uppercased()
    }

    public func chordParts(_ action: String) -> [String] {
        files["keymap.json"]?["chords"]?[action]?.strings() ?? []
    }

    public func formatChord(platform: String, action: String) -> String {
        chordParts(action).map { keyLabel(platform: platform, key: $0) }.joined(separator: "+")
    }

    /// macOS 的修饰键符号习惯连写（⌘K），Windows 用加号连接（Ctrl+K）。
    public func compactChord(platform: String, action: String) -> String {
        let parts = chordParts(action).map { keyLabel(platform: platform, key: $0) }
        return platform == "macos" ? parts.joined() : parts.joined(separator: "+")
    }

    public var triggers: [TriggerDef] {
        (files["triggers.json"]?["triggers"]?.values ?? []).map {
            TriggerDef(word: $0["word"]?.string ?? "", toolId: $0["toolId"]?.string ?? "",
                       label: $0["label"]?.string ?? "", placeholder: $0["placeholder"]?.string ?? "")
        }
    }

    public func trigger(word: String) -> (toolId: String, label: String, placeholder: String)? {
        for item in triggers where item.word.caseInsensitiveCompare(word) == .orderedSame {
            return (item.toolId, item.label, item.placeholder)
        }
        return nil
    }

    public func permissions(platform: String) -> [PermissionDef] {
        (files["permissions.json"]?["platforms"]?[platform]?.values ?? []).map {
            PermissionDef(id: $0["id"]?.string ?? "", title: $0["title"]?.string ?? "", reason: $0["reason"]?.string ?? "")
        }
    }

    public var stagedDiffs: [StagedDiff] {
        (files["platform-diffs.json"]?["staged"]?.values ?? []).map {
            StagedDiff(id: $0["id"]?.string ?? "", category: $0["category"]?.string ?? "", owner: $0["owner"]?.string ?? "",
                       dueAt: $0["dueAt"]?.string ?? "", parityPlan: $0["parityPlan"]?.string ?? "")
        }
    }

    public func transitions() -> [String: [String]] {
        map(in: "ui-state.json", key: "transitions")
    }

    public func escMap() -> [String: [String]] {
        map(in: "ui-state.json", key: "esc")
    }

    public func settingsDefaults() -> [String: Any] {
        files["schema/settings.json"]?["x-defaults"]?.object ?? [:]
    }

    public func starter() -> StarterCatalog {
        let root = files["starter-pack.json"]
        let groups = (root?["groups"]?.values ?? []).map { Self.groupSpec($0) }
        let items = (root?["items"]?.values ?? []).map { item in
            StarterEntry(
                id: item["id"]?.string ?? "",
                group: item["group"]?.string ?? "",
                windows: Self.side(item["windows"]),
                macos: Self.side(item["macos"])
            )
        }
        return StarterCatalog(ungrouped: Self.groupSpec(root?["ungrouped"]), groups: groups, items: items)
    }

    private static func groupSpec(_ node: JSON?) -> StarterGroupSpec {
        StarterGroupSpec(id: node?["id"]?.string ?? "", name: node?["name"]?.string ?? "", color: node?["color"]?.string ?? "")
    }

    private static func side(_ node: JSON?) -> StarterSide {
        StarterSide(title: node?["title"]?.string ?? "", paths: node?["paths"]?.strings() ?? [], names: node?["names"]?.strings() ?? [])
    }

    public func fixture(_ name: String) throws -> JSON {
        try JSON.load(url: root.appendingPathComponent("fixtures/\(name)"))
    }

    private func map(in file: String, key: String) -> [String: [String]] {
        var result: [String: [String]] = [:]
        for (name, value) in files[file]?[key]?.object ?? [:] {
            result[name] = JSON(raw: value).strings()
        }
        return result
    }

    private static let required = [
        "contract-version.json", "tokens.json", "layout.json", "ui-state.json", "triggers.json",
        "keymap.json", "i18n.json", "permissions.json", "platform-diffs.json",
        "schema/index-item.json", "schema/groups.json", "schema/clipboard.json", "schema/settings.json",
        "starter-pack.json"
    ]

    public static func hashTree(_ root: URL) throws -> String {
        let files = try FileManager.default.subpathsOfDirectory(atPath: root.path)
            .filter { !$0.hasSuffix(".DS_Store") }
            .sorted()
        var hasher = SHA256()
        for relative in files {
            let url = root.appendingPathComponent(relative)
            var isDir: ObjCBool = false
            guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir), !isDir.boolValue else { continue }
            hasher.update(data: Data(relative.utf8))
            hasher.update(data: Data([0]))
            hasher.update(data: try Data(contentsOf: url))
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }
}

public struct ContractError: Error, CustomStringConvertible {
    public var description: String
    public init(_ description: String) { self.description = description }
}
