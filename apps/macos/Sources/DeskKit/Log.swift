import Foundation

enum Log {
    private static let url: URL = {
        let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("DeskKit/logs", isDirectory: true)
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        return root.appendingPathComponent("deskkit.log")
    }()

    static func info(_ message: String) {
        write("INFO", message)
    }

    static func warn(_ message: String) {
        write("WARN", message)
    }

    private static func write(_ level: String, _ message: String) {
        let line = "\(ISO8601DateFormatter().string(from: Date())) \(level) \(message)\n"
        fputs(line, stderr)
        if let data = line.data(using: .utf8) {
            if FileManager.default.fileExists(atPath: url.path) {
                if let handle = try? FileHandle(forWritingTo: url) {
                    handle.seekToEndOfFile()
                    handle.write(data)
                    try? handle.close()
                }
            } else {
                try? data.write(to: url)
            }
        }
    }
}

enum Paths {
    static var root: URL {
        let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("DeskKit", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    static func file(_ name: String) -> URL { root.appendingPathComponent(name) }
}

enum Pinyin {
    static func variants(_ text: String) -> [String] {
        let mutable = NSMutableString(string: text)
        CFStringTransform(mutable, nil, kCFStringTransformToLatin, false)
        CFStringTransform(mutable, nil, kCFStringTransformStripCombiningMarks, false)
        let spaced = (mutable as String).lowercased()
        let full = spaced.replacingOccurrences(of: " ", with: "")
        let initials = spaced.split(separator: " ").compactMap { $0.first }.map { String($0) }.joined()
        return Array(Set([full, initials, text.lowercased()].filter { !$0.isEmpty }))
    }
}
