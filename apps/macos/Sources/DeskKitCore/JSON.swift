import Foundation

public struct JSON {
    public let raw: Any

    public static func load(url: URL) throws -> JSON {
        let data = try Data(contentsOf: url)
        let raw = try JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])
        return JSON(raw: raw)
    }

    static func parse(_ text: String) throws -> JSON {
        let data = Data(text.utf8)
        let raw = try JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])
        return JSON(raw: raw)
    }

    public var object: [String: Any]? { raw as? [String: Any] }
    public var array: [Any]? { raw as? [Any] }

    public var string: String? { raw as? String }

    public var bool: Bool? {
        guard let number = raw as? NSNumber else { return raw as? Bool }
        if CFGetTypeID(number) == CFBooleanGetTypeID() {
            return number.boolValue
        }
        return nil
    }

    public var double: Double? {
        if let number = raw as? NSNumber, CFGetTypeID(number) != CFBooleanGetTypeID() {
            return number.doubleValue
        }
        return nil
    }

    public var int: Int? {
        guard let double else { return nil }
        return Int(double)
    }

    public subscript(key: String) -> JSON? {
        guard let object, let value = object[key] else { return nil }
        return JSON(raw: value)
    }

    public subscript(index: Int) -> JSON? {
        guard let array, array.indices.contains(index) else { return nil }
        return JSON(raw: array[index])
    }

    public var values: [JSON] {
        (array ?? []).map { JSON(raw: $0) }
    }

    public func strings() -> [String] {
        values.compactMap(\.string)
    }
}
