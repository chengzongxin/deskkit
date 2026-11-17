import Foundation

public final class LauncherStateMachine {
    public private(set) var state = "HIDDEN"
    private let transitions: [String: [String]]
    private let esc: [String: [String]]
    private let log: (String) -> Void

    public init(store: ContractStore, log: @escaping (String) -> Void = { _ in }) {
        transitions = store.transitions()
        esc = store.escMap()
        self.log = log
    }

    public func transition(_ next: String) -> DkResult {
        let allowed = transitions[state] ?? []
        guard allowed.contains(next) else {
            log("拒绝状态转移 \(state) -> \(next)")
            return .fail("INVALID_ARG", "不能从 \(state) 进入 \(next)")
        }
        let from = state
        state = next
        log("状态转移 \(from) -> \(next)")
        return .success(state)
    }

    public func escape() -> DkResult {
        guard let next = esc[state]?.first else {
            log("Esc 在 \(state)，保持隐藏")
            return .success(state)
        }
        log("Esc 回退 \(state) -> \(next)")
        return transition(next)
    }

    public func escapeSequence() -> [String] {
        let copy = LauncherStateMachine(transitions: transitions, esc: esc, state: state, log: { _ in })
        var seen: [String] = []
        while copy.state != "HIDDEN" && seen.count < 8 {
            guard copy.escape().ok else { break }
            seen.append(copy.state)
        }
        return seen
    }

    private init(transitions: [String: [String]], esc: [String: [String]], state: String, log: @escaping (String) -> Void) {
        self.transitions = transitions
        self.esc = esc
        self.state = state
        self.log = log
    }
}

public struct IndexRow: Equatable {
    public var id: String
    public var kind: String
    public var title: String
    public var keywords: [String]
    public init(id: String, kind: String, title: String, keywords: [String]) {
        self.id = id
        self.kind = kind
        self.title = title
        self.keywords = keywords
    }
}

public enum KeywordMatcher {
    private static let kinds: Set<String> = ["app", "file", "url", "tool"]

    public static func match(_ rows: [IndexRow], query: String) -> [String] {
        let needle = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !needle.isEmpty else { return [] }
        return rows.compactMap { row in
            guard kinds.contains(row.kind) else { return nil }
            let titleHit = row.title.range(of: needle, options: .caseInsensitive) != nil
            let keywordHit = row.keywords.contains { $0.range(of: needle, options: .caseInsensitive) != nil }
            return (titleHit || keywordHit) ? row.id : nil
        }
    }
}

public enum TriggerTable {
    public static func resolve(_ store: ContractStore, word: String) -> String? {
        store.trigger(word: word)?.toolId
    }
}

public struct GroupRecord {
    public var id: String
    public var name: String
    public var color: String
    public var itemIds: [String]
    public init(id: String, name: String, color: String, itemIds: [String]) {
        self.id = id
        self.name = name
        self.color = color
        self.itemIds = itemIds
    }
}

public struct ItemRecord {
    public var id: String
    public var title: String
    public init(id: String, title: String) {
        self.id = id
        self.title = title
    }
}

public enum GroupEditor {
    public static func delete(groups: inout [GroupRecord], items: [ItemRecord], groupId: String, ungroupedId: String, ungroupedName: String) -> DkResult {
        guard groupId != ungroupedId else { return .fail("INVALID_ARG", "不能删除未分组") }
        guard let index = groups.firstIndex(where: { $0.id == groupId }) else {
            return .fail("INVALID_ARG", "分组不存在")
        }
        let moving = groups[index].itemIds
        groups.remove(at: index)
        if let slot = groups.firstIndex(where: { $0.id == ungroupedId }) {
            for id in moving where !groups[slot].itemIds.contains(id) {
                groups[slot].itemIds.append(id)
            }
        } else {
            groups.append(GroupRecord(id: ungroupedId, name: ungroupedName, color: "", itemIds: moving))
        }
        return .success(items.map(\.id).joined(separator: ","))
    }
}

public enum ExpressionEvaluator {
    public static func evaluate(_ input: String) -> DkResult {
        let trimmed = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return .fail("INVALID_ARG", "算式为空") }
        var parser = Parser(chars: Array(trimmed))
        do {
            let value = try parser.parseExpression()
            parser.skip()
            if !parser.end { return .fail("INVALID_ARG", "算式无法解析") }
            return .success(format(value))
        } catch is DivZero {
            return .fail("INVALID_ARG", "除数不能为 0")
        } catch {
            return .fail("INVALID_ARG", "算式无法解析")
        }
    }

    private static func format(_ value: Decimal) -> String {
        let number = NSDecimalNumber(decimal: value)
        if value == Decimal(number.intValue) && value >= Decimal(Int.min) && value <= Decimal(Int.max) && number.doubleValue.rounded() == number.doubleValue {
            return String(number.intValue)
        }
        let text = number.stringValue
        if text.contains("."), let decimal = Decimal(string: text), decimal == value {
            var copy = value
            var rounded = Decimal()
            NSDecimalRound(&rounded, &copy, 0, .plain)
            if rounded == value { return NSDecimalNumber(decimal: rounded).stringValue }
        }
        return text
    }

    private struct DivZero: Error {}
    private struct Bad: Error {}

    private struct Parser {
        var chars: [Character]
        var index = 0
        var end: Bool { index >= chars.count }
        mutating func skip() {
            while !end, chars[index].isWhitespace { index += 1 }
        }
        mutating func parseExpression() throws -> Decimal {
            var value = try parseTerm()
            while true {
                skip()
                if end || (chars[index] != "+" && chars[index] != "-") { return value }
                let op = chars[index]
                index += 1
                let right = try parseTerm()
                value = op == "+" ? value + right : value - right
            }
        }
        mutating func parseTerm() throws -> Decimal {
            var value = try parseFactor()
            while true {
                skip()
                if end || (chars[index] != "*" && chars[index] != "/") { return value }
                let op = chars[index]
                index += 1
                let right = try parseFactor()
                if op == "/" && right == 0 { throw DivZero() }
                value = op == "*" ? value * right : value / right
            }
        }
        mutating func parseFactor() throws -> Decimal {
            skip()
            if end { throw Bad() }
            if chars[index] == "(" {
                index += 1
                let value = try parseExpression()
                skip()
                if end || chars[index] != ")" { throw Bad() }
                index += 1
                return value
            }
            return try parseNumber()
        }
        mutating func parseNumber() throws -> Decimal {
            skip()
            let start = index
            if !end && (chars[index] == "+" || chars[index] == "-") { index += 1 }
            var digits = false
            while !end, chars[index].isNumber { digits = true; index += 1 }
            if !end, chars[index] == "." {
                index += 1
                while !end, chars[index].isNumber { digits = true; index += 1 }
            }
            let text = String(chars[start..<index])
            guard digits, let value = Decimal(string: text, locale: Locale(identifier: "en_US_POSIX")) else { throw Bad() }
            return value
        }
    }
}

public enum UnitConverter {
    private static let table: [String: (group: String, base: Decimal)] = [
        "m": ("len", 1), "km": ("len", 1000), "mi": ("len", Decimal(string: "1609.344")!), "cm": ("len", Decimal(string: "0.01")!),
        "kg": ("wt", 1), "g": ("wt", Decimal(string: "0.001")!), "lb": ("wt", Decimal(string: "0.45359237")!),
        "b": ("data", 1), "kb": ("data", 1024), "mb": ("data", 1_048_576),
        "cny": ("money", 1), "usd": ("money", Decimal(string: "7.12")!)
    ]

    public static func convert(_ input: String) -> String? {
        let pattern = #"^\s*([0-9]*\.?[0-9]+)\s*([A-Za-z]+)\s+to\s+([A-Za-z]+)\s*$"#
        guard let match = input.range(of: pattern, options: .regularExpression) else { return nil }
        let text = String(input[match])
        let parts = text.split(whereSeparator: { $0.isWhitespace })
        guard parts.count == 4, let amount = Decimal(string: String(parts[0]), locale: Locale(identifier: "en_US_POSIX")) else { return nil }
        let from = String(parts[1]).lowercased()
        let to = String(parts[3]).lowercased()
        if ["c", "f", "k"].contains(from) && ["c", "f", "k"].contains(to) {
            return formatTemp(amount, from: from, to: to)
        }
        guard let left = table[from], let right = table[to], left.group == right.group, right.base != 0 else { return nil }
        let value = amount * left.base / right.base
        return "\(ExpressionEvaluator.evaluate(NSDecimalNumber(decimal: value).stringValue).data) \(to)"
    }

    private static func formatTemp(_ amount: Decimal, from: String, to: String) -> String {
        let celsius: Decimal
        switch from {
        case "f": celsius = (amount - 32) * 5 / 9
        case "k": celsius = amount - Decimal(string: "273.15")!
        default: celsius = amount
        }
        let output: Decimal
        switch to {
        case "f": output = celsius * 9 / 5 + 32
        case "k": output = celsius + Decimal(string: "273.15")!
        default: output = celsius
        }
        return "\(NSDecimalNumber(decimal: output).stringValue) \(to)"
    }
}

public enum ColorConvert {
    public static func describe(_ input: String) -> String? {
        guard let rgb = parse(input) else { return nil }
        let hex = String(format: "#%02X%02X%02X", rgb.0, rgb.1, rgb.2)
        let hsl = toHSL(rgb.0, rgb.1, rgb.2)
        return "\(hex)  rgb(\(rgb.0),\(rgb.1),\(rgb.2))  hsl(\(hsl.0), \(hsl.1)%, \(hsl.2)%)"
    }

    public static func parse(_ input: String) -> (Int, Int, Int)? {
        var text = input.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.hasPrefix("#") { text.removeFirst() }
        if text.count == 3 {
            text = text.map { "\($0)\($0)" }.joined()
        }
        guard text.count == 6, let value = Int(text, radix: 16) else { return nil }
        return ((value >> 16) & 255, (value >> 8) & 255, value & 255)
    }

    private static func toHSL(_ r: Int, _ g: Int, _ b: Int) -> (Int, Int, Int) {
        let rf = Double(r) / 255, gf = Double(g) / 255, bf = Double(b) / 255
        let maxV = max(rf, gf, bf), minV = min(rf, gf, bf)
        let l = (maxV + minV) / 2
        var h = 0.0, s = 0.0
        if maxV != minV {
            let d = maxV - minV
            s = l > 0.5 ? d / (2 - maxV - minV) : d / (maxV + minV)
            switch maxV {
            case rf: h = (gf - bf) / d + (gf < bf ? 6 : 0)
            case gf: h = (bf - rf) / d + 2
            default: h = (rf - gf) / d + 4
            }
            h /= 6
        }
        return (Int((h * 360).rounded()), Int((s * 100).rounded()), Int((l * 100).rounded()))
    }
}

public enum TimestampConvert {
    public static func describe(_ input: String) -> String? {
        let text = input.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.allSatisfy(\.isNumber), text.count == 10 || text.count == 13, let raw = Double(text) {
            let seconds = text.count == 13 ? raw / 1000 : raw
            let formatter = DateFormatter()
            formatter.dateFormat = "yyyy-MM-dd HH:mm:ss"
            formatter.timeZone = .current
            return formatter.string(from: Date(timeIntervalSince1970: seconds))
        }
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd HH:mm:ss"
        formatter.timeZone = .current
        if let date = formatter.date(from: text) {
            return String(Int(date.timeIntervalSince1970))
        }
        return nil
    }
}

public enum Translator {
    private static let dictionary = [
        "hello world": "你好，世界", "hello": "你好", "world": "世界", "thank you": "谢谢",
        "thanks": "谢谢", "good morning": "早上好", "deskkit": "DeskKit", "search": "搜索",
        "file": "文件", "copy": "复制", "color": "颜色", "time": "时间"
    ]

    public static func translate(_ input: String) -> (text: String, offline: Bool) {
        let key = input.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if let hit = dictionary[key] { return (hit, true) }
        return ("未收录：\(input)", true)
    }
}
