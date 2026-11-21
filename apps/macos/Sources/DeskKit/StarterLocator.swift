import CryptoKit
import DeskKitCore
import Foundation

enum StarterLocator {
    static func find(_ items: [StarterEntry]) -> [StarterFound] {
        items.compactMap { item in
            let title = item.macos.title.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !title.isEmpty else { return nil }
            guard let path = item.macos.paths.first(where: { FileManager.default.fileExists(atPath: $0) }) else { return nil }
            return StarterFound(id: item.id, title: title, path: path, groupId: item.group)
        }
    }

    static func installed(taken paths: [String]) -> [StarterFound] {
        var taken = Set(paths.map { $0.lowercased() })
        var hits: [StarterFound] = []
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        for root in ["/Applications", "/System/Applications", home + "/Applications"] {
            collectApps(root, into: &hits, taken: &taken, depth: 0)
        }
        hits.sort { $0.title.localizedCaseInsensitiveCompare($1.title) == .orderedAscending }
        return Array(hits.prefix(150))
    }

    private static func collectApps(_ directory: String, into hits: inout [StarterFound], taken: inout Set<String>, depth: Int) {
        guard depth < 2, let names = try? FileManager.default.contentsOfDirectory(atPath: directory) else { return }
        for name in names where !name.hasPrefix(".") {
            let path = (directory as NSString).appendingPathComponent(name)
            if name.lowercased().hasSuffix(".app") {
                let title = (name as NSString).deletingPathExtension
                let key = path.lowercased()
                guard !title.localizedCaseInsensitiveContains("uninstall"), taken.insert(key).inserted else { continue }
                let digest = SHA256.hash(data: Data(key.utf8))
                let id = "app-" + digest.prefix(6).map { String(format: "%02x", $0) }.joined()
                hits.append(StarterFound(id: id, title: title, path: path, groupId: "apps"))
                continue
            }
            var directoryFlag: ObjCBool = false
            guard FileManager.default.fileExists(atPath: path, isDirectory: &directoryFlag), directoryFlag.boolValue else { continue }
            collectApps(path, into: &hits, taken: &taken, depth: depth + 1)
        }
    }
}
