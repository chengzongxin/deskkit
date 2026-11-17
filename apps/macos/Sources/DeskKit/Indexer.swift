import AppKit
import DeskKitCore
import Foundation

struct Hit {
    var id: String
    var kind: String
    var title: String
    var subtitle: String
    var path: String
    var keywords: [String]
    var toolId: String?
    var copyText: String?
    var score: Int
}

enum Catalog {
    static let tools: [(id: String, title: String, words: [String])] = [
        ("clipboard", "剪贴板", ["clip", "history", "复制"]),
        ("picker", "截图取色", ["color", "picker", "取色"]),
        ("translate", "翻译", ["fy", "translate"]),
        ("calc", "计算器", ["js", "calc", "算式"]),
        ("timestamp", "时间戳", ["ts", "time", "日期"]),
        ("unit", "单位换算", ["unit", "km", "cny"]),
        ("color", "颜色转换", ["hex", "rgb", "hsl"]),
        ("qr", "二维码", ["qr"])
    ]

    static func toolHits() -> [Hit] {
        tools.map { tool in
            var words = tool.words + [tool.title]
            words.append(contentsOf: Pinyin.variants(tool.title))
            return Hit(id: "tool-\(tool.id)", kind: "tool", title: tool.title, subtitle: "内置工具", path: "", keywords: words, toolId: tool.id, copyText: nil, score: 40)
        }
    }
}

enum Indexer {
    static func collect() -> [Hit] {
        var hits = Catalog.toolHits()
        hits.append(contentsOf: applications())
        hits.append(contentsOf: files())
        Log.info("索引完成，\(hits.count) 条")
        return hits
    }

    private static func applications() -> [Hit] {
        let roots = ["/Applications", "/System/Applications", NSHomeDirectory() + "/Applications", "/System/Applications/Utilities"]
        var hits: [Hit] = []
        for root in roots {
            let url = URL(fileURLWithPath: root)
            guard let items = try? FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: nil) else { continue }
            for item in items where item.pathExtension == "app" {
                let title = item.deletingPathExtension().lastPathComponent
                var words = [title.lowercased()]
                words.append(contentsOf: Pinyin.variants(title))
                hits.append(Hit(id: "app-\(item.path)", kind: "app", title: title, subtitle: item.path, path: item.path, keywords: words, toolId: nil, copyText: nil, score: 30))
            }
        }
        return hits
    }

    private static func files() -> [Hit] {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let roots = ["Desktop", "Documents", "Downloads"].map { home.appendingPathComponent($0) }
        var hits: [Hit] = []
        for root in roots {
            guard let children = try? FileManager.default.contentsOfDirectory(at: root, includingPropertiesForKeys: [.isDirectoryKey], options: [.skipsHiddenFiles]) else { continue }
            for child in children {
                append(child, into: &hits, depth: 0)
                if hits.count > 600 { return hits }
            }
        }
        return hits
    }

    private static func append(_ url: URL, into hits: inout [Hit], depth: Int) {
        if url.pathExtension == "app" { return }
        let name = url.lastPathComponent
        var words = [name.lowercased()]
        words.append(contentsOf: Pinyin.variants(name))
        let isDir = (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) ?? false
        hits.append(Hit(id: "file-\(url.path)", kind: "file", title: name, subtitle: url.path, path: url.path, keywords: words, toolId: nil, copyText: nil, score: 20))
        guard isDir, depth < 1, let children = try? FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]) else { return }
        for child in children.prefix(40) {
            append(child, into: &hits, depth: depth + 1)
        }
    }
}
