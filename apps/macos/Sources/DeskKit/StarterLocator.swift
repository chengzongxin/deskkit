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
}
