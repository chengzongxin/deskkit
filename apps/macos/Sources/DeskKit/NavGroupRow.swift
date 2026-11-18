import AppKit

/// 侧栏分组行：点击选择，握把拖拽排序，右键菜单。
final class NavGroupRow: HitRow {
    var groupId = ""
    var selected = false { didSet { refreshChrome() } }
    var accentSoft: NSColor = .clear
    var accent: NSColor = .systemBlue
    var onReorder: ((String, String, Bool) -> Void)?

    private let grip = NavGroupGripView()
    private let insertLine = NSView()
    private var insertAfter = false
    private var dropActive = false { didSet { refreshChrome() } }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        registerForDraggedTypes([.string])
        wantsLayer = true
        layer?.cornerRadius = 8

        insertLine.isHidden = true
        insertLine.wantsLayer = true
        addSubview(insertLine)

        grip.groupIdProvider = { [weak self] in self?.groupId ?? "" }
        grip.onDragEnded = { [weak self] in self?.alphaValue = 1 }
        addSubview(grip)
    }

    required init?(coder: NSCoder) { nil }

    override func layout() {
        super.layout()
        let gripW: CGFloat = 18
        grip.frame = NSRect(x: bounds.width - gripW - 6, y: (bounds.height - 22) / 2, width: gripW, height: 22)
        let lineY = insertAfter ? bounds.height - 2 : 0
        insertLine.frame = NSRect(x: 6, y: lineY, width: bounds.width - 12, height: 2)
    }

    override func mouseUp(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        if grip.frame.contains(point) {
            return
        }

        onClick()
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        guard let source = sender.draggingPasteboard.string(forType: .string), source != groupId else {
            return []
        }

        updateInsert(from: sender)
        return .move
    }

    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        guard let source = sender.draggingPasteboard.string(forType: .string), source != groupId else {
            return []
        }

        updateInsert(from: sender)
        return .move
    }

    override func draggingExited(_ sender: NSDraggingInfo?) {
        clearDropHint()
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        defer { clearDropHint() }
        guard let source = sender.draggingPasteboard.string(forType: .string), source != groupId else {
            return false
        }

        updateInsert(from: sender)
        onReorder?(source, groupId, insertAfter)
        return true
    }

    private func updateInsert(from sender: NSDraggingInfo) {
        let point = convert(sender.draggingLocation, from: nil)
        let after = point.y > bounds.height / 2
        showInsert(after: after)
    }

    private func showInsert(after: Bool) {
        dropActive = true
        if insertAfter == after, !insertLine.isHidden {
            return
        }

        insertAfter = after
        insertLine.isHidden = false
        insertLine.layer?.backgroundColor = accent.cgColor
        setNeedsLayout()
        layout()
        refreshChrome()
    }

    private func clearDropHint() {
        dropActive = false
        insertLine.isHidden = true
        refreshChrome()
    }

    private func refreshChrome() {
        if dropActive {
            layer?.backgroundColor = (selected ? accentSoft : NSColor.clear.withAlphaComponent(0.08)).cgColor
        } else if selected {
            layer?.backgroundColor = accentSoft.cgColor
        } else {
            layer?.backgroundColor = NSColor.clear.cgColor
        }
    }
}

private final class NavGroupGripView: NSView, NSDraggingSource {
    var groupIdProvider: (() -> String)?
    var onDragEnded: (() -> Void)?

    private var press: NSPoint?
    private var started = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        let label = NSTextField(labelWithString: "⋮⋮")
        label.font = NSFont.systemFont(ofSize: 11)
        label.textColor = NSColor.secondaryLabelColor
        label.alignment = .center
        label.frame = bounds
        label.autoresizingMask = [.width, .height]
        addSubview(label)
        toolTip = "拖动排序"
    }

    required init?(coder: NSCoder) { nil }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .openHand)
    }

    override func mouseDown(with event: NSEvent) {
        press = convert(event.locationInWindow, from: nil)
        started = false
    }

    override func mouseDragged(with event: NSEvent) {
        guard let press, !started else { return }
        let now = convert(event.locationInWindow, from: nil)
        let dx = now.x - press.x
        let dy = now.y - press.y
        if (dx * dx + dy * dy) < 25 { return }
        started = true
        guard let id = groupIdProvider?(), !id.isEmpty else { return }

        superview?.alphaValue = 0.5
        let item = NSPasteboardItem()
        item.setString(id, forType: .string)
        let dragItem = NSDraggingItem(pasteboardWriter: item)
        dragItem.setDraggingFrame(superview?.bounds ?? bounds, contents: nil)
        beginDraggingSession(with: [dragItem], event: event, source: self)
    }

    override func mouseUp(with event: NSEvent) {
        press = nil
        started = false
    }

    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation {
        .move
    }

    func draggingSession(_ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation) {
        started = false
        press = nil
        onDragEnded?()
    }
}