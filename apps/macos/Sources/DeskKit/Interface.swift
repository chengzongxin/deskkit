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

    static func plain(_ text: String, _ size: CGFloat, _ weight: NSFont.Weight, _ color: NSColor, _ align: NSTextAlignment = .left) -> PlainLabel {
        let label = PlainLabel()
        label.text = text
        label.font = font(size, weight)
        label.color = color
        label.alignment = align
        return label
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

class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

/// 顶栏、底栏和侧栏空白处按住可以拖动窗口。禁止 `mouseDownCanMoveWindow`，否则子视图里的拖拽排序会被当成挪窗口。
final class WindowDragView: FlippedView {
    var onDragEnd: (() -> Void)?
    var onRightClick: ((NSEvent) -> Void)?

    override var mouseDownCanMoveWindow: Bool { false }

    override func rightMouseDown(with event: NSEvent) {
        onRightClick?(event)
    }

    override func mouseDown(with event: NSEvent) {
        let local = convert(event.locationInWindow, from: nil)
        guard bounds.contains(local) else { return }
        if let hit = hitTest(local), hit !== self {
            return
        }
        guard let window else { return }
        window.performDrag(with: event)
        onDragEnd?()
    }
}

/// 无边框按钮默认只认图标上的不透明像素，点在按钮空白处会穿透。这里整块区域都可点。
final class SolidButton: NSButton {
    override func hitTest(_ point: NSPoint) -> NSView? {
        guard isEnabled, !isHidden, alphaValue > 0.01 else { return nil }
        let local = convert(point, from: superview)
        return bounds.contains(local) ? self : nil
    }
}

/// 拖拽提示层不拦截命中，方便判断鼠标下面是哪个分组。
final class PassThroughView: FlippedView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

/// 快捷启动面板接收 Finder 拖入的应用、文件、文件夹和网址。
/// 自身不用图层画背景，把不透明的圆角画进窗口，系统才能对透明窗口做拖放命中。
final class PanelDropView: FlippedView {
    static let dragTypes: [NSPasteboard.PasteboardType] = [
        .fileURL,
        .URL,
        .string,
        NSPasteboard.PasteboardType("public.file-url"),
        NSPasteboard.PasteboardType("public.url"),
        NSPasteboard.PasteboardType("public.utf8-plain-text"),
        NSPasteboard.PasteboardType("NSFilenamesPboardType"),
        NSPasteboard.PasteboardType("com.apple.pasteboard.promised-file-url")
    ]

    var onUpdate: ((NSDraggingInfo) -> NSDragOperation)?
    var onExit: (() -> Void)?
    var onPerform: ((NSDraggingInfo) -> Bool)?
    var onResizeEnd: (() -> Void)?
    var fill: NSColor = .windowBackgroundColor { didSet { needsDisplay = true } }

    private let resizeThickness: CGFloat = 7

    private struct ResizeEdge: OptionSet {
        let rawValue: Int
        static let left = ResizeEdge(rawValue: 1)
        static let right = ResizeEdge(rawValue: 2)
        static let top = ResizeEdge(rawValue: 4)
        static let bottom = ResizeEdge(rawValue: 8)
    }

    override var isOpaque: Bool { false }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        registerForDraggedTypes(Self.dragTypes)
    }

    required init?(coder: NSCoder) { nil }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        registerForDraggedTypes(Self.dragTypes)
    }

    override func draw(_ dirtyRect: NSRect) {
        let path = NSBezierPath(roundedRect: bounds, xRadius: 14, yRadius: 14)
        fill.setFill()
        path.fill()
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        let types = (sender.draggingPasteboard.types ?? []).map(\.rawValue).joined(separator: ",")
        Log.info("拖拽进入 类型=\(types)")
        return onUpdate?(sender) ?? []
    }

    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        onUpdate?(sender) ?? []
    }

    override func draggingExited(_ sender: NSDraggingInfo?) {
        onExit?()
    }

    override func draggingEnded(_ sender: NSDraggingInfo) {
        onExit?()
    }

    override func prepareForDragOperation(_ sender: NSDraggingInfo) -> Bool {
        let operation = onUpdate?(sender) ?? []
        return !operation.isEmpty
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        onPerform?(sender) ?? false
    }

    override func hitTest(_ point: NSPoint) -> NSView? {
        resizeEdges(at: point).isEmpty ? super.hitTest(point) : self
    }

    override func resetCursorRects() {
        super.resetCursorRects()
        let thickness = resizeThickness
        addCursorRect(NSRect(x: 0, y: 0, width: thickness, height: bounds.height), cursor: resizeCursor(.left))
        addCursorRect(NSRect(x: bounds.width - thickness, y: 0, width: thickness, height: bounds.height), cursor: resizeCursor(.right))
        addCursorRect(NSRect(x: 0, y: 0, width: bounds.width, height: thickness), cursor: resizeCursor(.top))
        addCursorRect(NSRect(x: 0, y: bounds.height - thickness, width: bounds.width, height: thickness), cursor: resizeCursor(.bottom))
        let corner: CGFloat = 14
        addCursorRect(NSRect(x: 0, y: 0, width: corner, height: corner), cursor: resizeCursor([.top, .left]))
        addCursorRect(NSRect(x: bounds.width - corner, y: 0, width: corner, height: corner), cursor: resizeCursor([.top, .right]))
        addCursorRect(NSRect(x: 0, y: bounds.height - corner, width: corner, height: corner), cursor: resizeCursor([.bottom, .left]))
        addCursorRect(NSRect(x: bounds.width - corner, y: bounds.height - corner, width: corner, height: corner), cursor: resizeCursor([.bottom, .right]))
    }

    override func mouseDown(with event: NSEvent) {
        let edges = resizeEdges(at: convert(event.locationInWindow, from: nil))
        guard !edges.isEmpty, let window else {
            super.mouseDown(with: event)
            return
        }
        let minSize = window.minSize
        let startFrame = window.frame
        let startMouse = NSEvent.mouseLocation
        var changed = false
        while true {
            guard let next = window.nextEvent(matching: [.leftMouseDragged, .leftMouseUp]) else { break }
            if next.type == .leftMouseUp { break }
            let mouse = NSEvent.mouseLocation
            let dx = mouse.x - startMouse.x
            let dy = mouse.y - startMouse.y
            var frame = startFrame
            if edges.contains(.right) {
                frame.size.width = max(minSize.width, startFrame.width + dx)
            }
            if edges.contains(.left) {
                let width = max(minSize.width, startFrame.width - dx)
                frame.origin.x = startFrame.maxX - width
                frame.size.width = width
            }
            if edges.contains(.top) {
                frame.size.height = max(minSize.height, startFrame.height + dy)
            }
            if edges.contains(.bottom) {
                let height = max(minSize.height, startFrame.height - dy)
                frame.origin.y = startFrame.maxY - height
                frame.size.height = height
            }
            if frame != startFrame { changed = true }
            window.setFrame(frame, display: true)
        }
        if changed { onResizeEnd?() }
    }

    private func resizeEdges(at point: NSPoint) -> ResizeEdge {
        var edges: ResizeEdge = []
        if point.x <= resizeThickness { edges.insert(.left) }
        if point.x >= bounds.width - resizeThickness { edges.insert(.right) }
        if point.y <= resizeThickness { edges.insert(.top) }
        if point.y >= bounds.height - resizeThickness { edges.insert(.bottom) }
        return edges
    }

    private func resizeCursor(_ edges: ResizeEdge) -> NSCursor {
        if #available(macOS 15.0, *) {
            switch edges {
            case [.top, .left]:
                return NSCursor.frameResize(position: .topLeft, directions: .all)
            case [.top, .right]:
                return NSCursor.frameResize(position: .topRight, directions: .all)
            case [.bottom, .left]:
                return NSCursor.frameResize(position: .bottomLeft, directions: .all)
            case [.bottom, .right]:
                return NSCursor.frameResize(position: .bottomRight, directions: .all)
            case [.left], [.right]:
                return NSCursor.frameResize(position: edges.contains(.left) ? .left : .right, directions: .all)
            default:
                return NSCursor.frameResize(position: edges.contains(.top) ? .top : .bottom, directions: .all)
            }
        }
        if edges.contains(.left) || edges.contains(.right) {
            return edges.contains(.top) || edges.contains(.bottom) ? .crosshair : .resizeLeftRight
        }
        return .resizeUpDown
    }
}

/// 单行文本，按字面宽度绘制，不走文本框的内边距，避免被裁成省略号。
final class PlainLabel: NSView {
    var text = "" { didSet { needsDisplay = true } }
    var color: NSColor = .secondaryLabelColor { didSet { needsDisplay = true } }
    var font: NSFont = .systemFont(ofSize: 11) { didSet { needsDisplay = true } }
    var alignment: NSTextAlignment = .left { didSet { needsDisplay = true } }
    var truncates = false { didSet { needsDisplay = true } }
    override var isFlipped: Bool { true }

    static func width(for text: String, font: NSFont) -> CGFloat {
        ceil((text as NSString).size(withAttributes: [.font: font]).width)
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func draw(_ dirtyRect: NSRect) {
        let style = NSMutableParagraphStyle()
        style.alignment = alignment
        style.lineBreakMode = truncates ? .byTruncatingTail : .byClipping
        let attributes: [NSAttributedString.Key: Any] = [
            .font: font,
            .foregroundColor: color,
            .paragraphStyle: style
        ]
        let size = (text as NSString).size(withAttributes: attributes)
        let rect = NSRect(x: 0, y: (bounds.height - ceil(size.height)) / 2, width: bounds.width, height: ceil(size.height))
        (text as NSString).draw(in: rect, withAttributes: attributes)
    }
}

/// 让输入文字在输入行里垂直居中，并可给占位文字留出左右边距。
final class CenteredFieldCell: NSTextFieldCell {
    var textInset: CGFloat = 0

    override func drawingRect(forBounds rect: NSRect) -> NSRect {
        let inset = rect.insetBy(dx: textInset, dy: 0)
        let sample = (stringValue.isEmpty ? (placeholderString ?? "") : stringValue) as NSString
        let size = sample.size(withAttributes: [.font: font ?? .systemFont(ofSize: 18)])
        var title = super.drawingRect(forBounds: inset)
        if size.height > 0, size.height < title.height {
            title.origin.y += floor((title.height - size.height) / 2)
            title.size.height = ceil(size.height)
        }
        return title
    }

    override func titleRect(forBounds rect: NSRect) -> NSRect {
        drawingRect(forBounds: rect)
    }

    override func edit(withFrame rect: NSRect, in controlView: NSView, editor textObj: NSText, delegate: Any?, event: NSEvent?) {
        super.edit(withFrame: drawingRect(forBounds: rect), in: controlView, editor: textObj, delegate: delegate, event: event)
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

class HitRow: NSView {
    var onClick: () -> Void = {}
    var onRightClick: ((NSEvent) -> Void)?
    var onLongPress: ((NSEvent) -> Void)?
    var onHover: ((Bool) -> Void)?
    var swallowsClicks = false
    private var suppressClick = false
    override var isFlipped: Bool { true }
    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        guard bounds.contains(local) else { return nil }
        if swallowsClicks { return self }
        for child in subviews.reversed() {
            if let hit = child.hitTest(local) { return hit }
        }
        return self
    }
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach { removeTrackingArea($0) }
        let area = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self, userInfo: nil)
        addTrackingArea(area)
    }
    override func mouseEntered(with event: NSEvent) { onHover?(true) }
    override func mouseExited(with event: NSEvent) { onHover?(false) }
    override func mouseDown(with event: NSEvent) {
        guard let onLongPress, let window else { return }
        let deadline = Date().addingTimeInterval(0.25)
        while Date() < deadline {
            let remaining = deadline.timeIntervalSinceNow
            guard remaining > 0 else { break }
            guard let next = window.nextEvent(matching: [.leftMouseDragged, .leftMouseUp], until: Date().addingTimeInterval(remaining), inMode: .eventTracking, dequeue: true) else {
                break
            }
            if next.type == .leftMouseUp {
                onClick()
                return
            }
        }
        suppressClick = true
        onLongPress(event)
    }
    override func mouseUp(with event: NSEvent) {
        if suppressClick {
            suppressClick = false
            return
        }
        onClick()
    }
    override func rightMouseDown(with event: NSEvent) { onRightClick?(event) }
}

/// 自身不翻转，避免在翻转父视图里把应用图标画反或画空。
final class IconView: NSView {
    var image: NSImage? { didSet { needsDisplay = true } }
    override var isFlipped: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

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
