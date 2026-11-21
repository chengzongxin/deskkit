import Foundation

public struct StarterSide {
    public var title: String
    public var paths: [String]
    public var names: [String]
}

public struct StarterEntry {
    public var id: String
    public var group: String
    public var windows: StarterSide
    public var macos: StarterSide
}

public struct StarterGroupSpec {
    public var id: String
    public var name: String
    public var color: String
}

public struct StarterCatalog {
    public var ungrouped: StarterGroupSpec
    public var groups: [StarterGroupSpec]
    public var items: [StarterEntry]
}

public struct StarterFound {
    public var id: String
    public var title: String
    public var path: String
    public var groupId: String

    public init(id: String, title: String, path: String, groupId: String) {
        self.id = id
        self.title = title
        self.path = path
        self.groupId = groupId
    }
}

public struct StarterShortcut {
    public var id: String
    public var title: String
    public var path: String
    public var kind: String
}

public struct StarterBuilt {
    public var groups: [GroupRecord]
    public var items: [StarterShortcut]
}

public enum StarterPack {
    public static func compose(groups: [StarterGroupSpec], found: [StarterFound], ungrouped: StarterGroupSpec) -> StarterBuilt {
        var byGroup: [String: [StarterFound]] = [:]
        for item in found {
            let path = item.path.trimmingCharacters(in: .whitespacesAndNewlines)
            let id = item.id.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !path.isEmpty, !id.isEmpty else { continue }
            byGroup[item.groupId, default: []].append(StarterFound(id: id, title: item.title, path: path, groupId: item.groupId))
        }

        var builtGroups: [GroupRecord] = []
        var shortcuts: [StarterShortcut] = []
        for group in groups where group.id != ungrouped.id {
            guard let list = byGroup[group.id], !list.isEmpty else { continue }
            var ids: [String] = []
            for item in list {
                ids.append(item.id)
                shortcuts.append(StarterShortcut(id: item.id, title: item.title, path: item.path, kind: kindOf(item.path)))
            }
            builtGroups.append(GroupRecord(id: group.id, name: group.name, color: group.color, itemIds: ids))
        }
        builtGroups.append(GroupRecord(id: ungrouped.id, name: ungrouped.name, color: ungrouped.color, itemIds: []))
        return StarterBuilt(groups: builtGroups, items: shortcuts)
    }

    public static func kindOf(_ path: String) -> String {
        let ext = (path as NSString).pathExtension.lowercased()
        if ext == "exe" || ext == "lnk" || ext == "app" { return "app" }
        return "file"
    }
}
