import AppKit
import DeskKitCore

enum Theme {
    static func color(_ store: ContractStore, _ name: String) -> NSColor { paint(store.color(name)) }

    static func paint(_ hex: String) -> NSColor {
        var text = hex
        if text.hasPrefix("#") { text.removeFirst() }
        let value = Int(text, radix: 16) ?? 0
        return NSColor(
            srgbRed: CGFloat((value >> 16) & 255) / 255,
            green: CGFloat((value >> 8) & 255) / 255,
            blue: CGFloat(value & 255) / 255,
            alpha: 1
        )
    }
}

/// 无边框浮层必须显式声明可成为 key，否则输入框拿不到键盘。
final class KeyPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

final class KeyWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// 占位文案叠在输入框上时，必须穿透点击，否则点输入区无法聚焦。
final class PassThroughLabel: NSTextField {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

enum UI {
    static func font(_ size: CGFloat, _ weight: NSFont.Weight = .regular) -> NSFont {
        NSFont.systemFont(ofSize: size, weight: weight)
    }

    static func label(_ text: String, _ size: CGFloat, _ weight: NSFont.Weight, _ color: NSColor) -> NSTextField {
        let field = NSTextField(labelWithString: text)
        field.font = font(size, weight)
        field.textColor = color
        field.lineBreakMode = .byTruncatingTail
        field.maximumNumberOfLines = 1
        field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        return field
    }

    static func wrap(_ text: String, _ size: CGFloat, _ weight: NSFont.Weight, _ color: NSColor, width: CGFloat) -> NSTextField {
        let field = NSTextField(wrappingLabelWithString: text)
        field.font = font(size, weight)
        field.textColor = color
        field.preferredMaxLayoutWidth = width
        field.maximumNumberOfLines = 0
        return field
    }
}

final class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

/// 单行文本，按字面宽度绘制，不走文本框的内边距，避免被裁成省略号。
final class PlainLabel: NSView {
    var text = "" { didSet { needsDisplay = true } }
    var color: NSColor = .secondaryLabelColor { didSet { needsDisplay = true } }
    var font: NSFont = .systemFont(ofSize: 11) { didSet { needsDisplay = true } }
    override var isFlipped: Bool { true }

    static func width(for text: String, font: NSFont) -> CGFloat {
        ceil((text as NSString).size(withAttributes: [.font: font]).width)
    }

    override func draw(_ dirtyRect: NSRect) {
        let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: color]
        let size = (text as NSString).size(withAttributes: attributes)
        let rect = NSRect(x: 0, y: (bounds.height - size.height) / 2, width: ceil(size.width), height: ceil(size.height))
        (text as NSString).draw(in: rect, withAttributes: attributes)
    }
}

/// 让输入文字在输入行里垂直居中。
final class CenteredFieldCell: NSTextFieldCell {
    override func drawingRect(forBounds rect: NSRect) -> NSRect {
        let sample = (stringValue.isEmpty ? (placeholderString ?? "") : stringValue) as NSString
        let size = sample.size(withAttributes: [.font: font ?? .systemFont(ofSize: 18)])
        var title = super.drawingRect(forBounds: rect)
        if size.height > 0, size.height < title.height {
            title.origin.y += floor((title.height - size.height) / 2)
            title.size.height = ceil(size.height)
        }
        return title
    }

    override func select(withFrame rect: NSRect, in controlView: NSView, editor textObj: NSText, delegate: Any?, start selStart: Int, length selLength: Int) {
        let box = drawingRect(forBounds: rect)
        super.select(withFrame: box, in: controlView, editor: textObj, delegate: delegate, start: selStart, length: selLength)
    }
}
final class KeycapLabel: NSView {
    var text = "" { didSet { needsDisplay = true } }
    var tint: NSColor = .secondaryLabelColor { didSet { needsDisplay = true } }
    var border: NSColor = .separatorColor { didSet { needsDisplay = true } }
    var fill: NSColor = .white { didSet { needsDisplay = true } }
    override var isFlipped: Bool { true }

    static func width(for text: String) -> CGFloat {
        let font = NSFont.systemFont(ofSize: 11, weight: .semibold)
        return ceil((text as NSString).size(withAttributes: [.font: font]).width) + 12
    }

    override func draw(_ dirtyRect: NSRect) {
        let box = bounds.insetBy(dx: 0.5, dy: 0.5)
        let path = NSBezierPath(roundedRect: box, xRadius: 5, yRadius: 5)
        fill.setFill()
        path.fill()
        border.setStroke()
        path.lineWidth = 1
        path.stroke()
        let font = NSFont.systemFont(ofSize: 11, weight: .semibold)
        let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: tint]
        let size = (text as NSString).size(withAttributes: attributes)
        let rect = NSRect(
            x: (bounds.width - size.width) / 2,
            y: (bounds.height - size.height) / 2,
            width: size.width,
            height: size.height
        )
        (text as NSString).draw(in: rect, withAttributes: attributes)
    }
}

final class KeycapControl: NSControl {
    var text = "" { didSet { needsDisplay = true } }
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        let box = bounds.insetBy(dx: 0.5, dy: 0.5)
        let path = NSBezierPath(roundedRect: box, xRadius: 6, yRadius: 6)
        NSColor.white.setFill()
        path.fill()
        NSColor(white: 0.75, alpha: 1).setStroke()
        path.lineWidth = 1
        path.stroke()
        let font = NSFont.systemFont(ofSize: 12, weight: .semibold)
        let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: NSColor(white: 0.35, alpha: 1)]
        let size = (text as NSString).size(withAttributes: attributes)
        let rect = NSRect(
            x: (bounds.width - size.width) / 2,
            y: (bounds.height - size.height) / 2,
            width: size.width,
            height: size.height
        )
        (text as NSString).draw(in: rect, withAttributes: attributes)
    }
    override func mouseUp(with event: NSEvent) {
        if bounds.contains(convert(event.locationInWindow, from: nil)) {
            sendAction(action, to: target)
        }
    }
}

final class HitRow: NSView {
    var onClick: () -> Void = {}
    var onRightClick: ((NSEvent) -> Void)?
    var swallowsClicks = false
    override var isFlipped: Bool { true }
    override func hitTest(_ point: NSPoint) -> NSView? {
        guard swallowsClicks else { return super.hitTest(point) }
        let local = convert(point, from: superview)
        return bounds.contains(local) ? self : nil
    }
    override func mouseUp(with event: NSEvent) { onClick() }
    override func rightMouseDown(with event: NSEvent) { onRightClick?(event) }
}

/// 自身不翻转，避免在翻转父视图里把应用图标画反或画空。
final class IconView: NSView {
    var image: NSImage? { didSet { needsDisplay = true } }
    override var isFlipped: Bool { false }
    override func draw(_ dirtyRect: NSRect) {
        guard let image else { return }
        image.draw(
            in: bounds,
            from: .zero,
            operation: .sourceOver,
            fraction: 1,
            respectFlipped: false,
            hints: [.interpolation: NSNumber(value: NSImageInterpolation.high.rawValue)]
        )
    }
}

enum AppIcons {
    private static let cache = NSCache<NSString, NSImage>()

    static func file(_ path: String, size: CGFloat) -> NSImage? {
        guard !path.isEmpty, FileManager.default.fileExists(atPath: path) else { return nil }
        let key = "\(path)#\(Int(size))" as NSString
        if let cached = cache.object(forKey: key) { return cached }
        let icon = NSWorkspace.shared.icon(forFile: path)
        icon.size = NSSize(width: size, height: size)
        cache.setObject(icon, forKey: key)
        return icon
    }

    static func symbol(_ name: String, size: CGFloat, tint: NSColor) -> NSImage {
        let key = "sym:\(name)#\(Int(size))#\(tint.hashValue)" as NSString
        if let cached = cache.object(forKey: key) { return cached }
        let config = NSImage.SymbolConfiguration(pointSize: size * 0.72, weight: .medium)
            .applying(NSImage.SymbolConfiguration(paletteColors: [tint]))
        let image = NSImage(systemSymbolName: name, accessibilityDescription: nil)?
            .withSymbolConfiguration(config) ?? NSImage()
        image.size = NSSize(width: size, height: size)
        cache.setObject(image, forKey: key)
        return image
    }

    static func symbolName(toolId: String?, kind: String) -> String {
        switch toolId ?? kind {
        case "clipboard": return "doc.on.clipboard"
        case "picker": return "eyedropper"
        case "translate": return "character.bubble"
        case "calc": return "plus.forwardslash.minus"
        case "timestamp": return "clock"
        case "unit": return "arrow.left.arrow.right"
        case "color": return "paintpalette"
        case "qr": return "qrcode"
        case "url": return "link"
        case "file": return "doc"
        default: return "app.dashed"
        }
    }

    static func image(for hit: Hit, size: CGFloat, tint: NSColor) -> NSImage {
        if hit.kind != "tool", let icon = file(hit.path, size: size) {
            return icon
        }
        if hit.kind == "app" {
            Log.warn("应用没有图标 \(hit.title) 路径 \(hit.path)")
        }
        return symbol(symbolName(toolId: hit.toolId, kind: hit.kind), size: size, tint: tint)
    }

    static func view(image: NSImage, frame: NSRect) -> IconView {
        let view = IconView(frame: frame)
        view.image = image
        return view
    }
}

final class SwitchView: NSControl {
    var on = false { didSet { needsDisplay = true } }
    var onColor: NSColor = .systemBlue
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        let track = NSBezierPath(roundedRect: NSRect(x: 2, y: 2, width: 36, height: 20), xRadius: 10, yRadius: 10)
        (on ? onColor : NSColor(white: 0.86, alpha: 1)).setFill()
        track.fill()
        NSColor.white.setFill()
        NSBezierPath(ovalIn: NSRect(x: on ? 20 : 4, y: 4, width: 16, height: 16)).fill()
    }
    override func mouseUp(with event: NSEvent) {
        on.toggle()
        sendAction(action, to: target)
    }
}

final class ActionBox: NSObject {
    static var key: UInt8 = 0
    let work: () -> Void
    init(_ work: @escaping () -> Void) { self.work = work }
    @objc func run() { work() }
}
