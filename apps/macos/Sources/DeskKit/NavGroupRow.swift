import AppKit

extension NSPasteboard.PasteboardType {
    static let deskkitGroup = NSPasteboard.PasteboardType("deskkit-group")
}

/// 侧栏分组行：点击选择，右侧握把手动拖放排序，右键菜单。
final class NavGroupRow: HitRow {
    var groupId = ""
    var selected = false { didSet { refreshChrome() } }
    var accentSoft: NSColor = .clear
    var accent: NSColor = .systemBlue

    private let grip = NavGroupGripView()
    private let insertLine = NSView()
    private let dot = NSView()
    private let titleSlot = NSView()
    private let countLabel = PlainLabel()
    private var insertAfter = false
    private var hovered = false
    private var dropActive = false { didSet { refreshChrome() } }

    private let handleWidth: CGFloat = 52
    private let gripWidth: CGFloat = 22
    private let countWidth: CGFloat = 28
    private let trailingPad: CGFloat = 6

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.cornerRadius = 8

        insertLine.isHidden = true
        insertLine.wantsLayer = true
        addSubview(insertLine)

        dot.wantsLayer = true
        dot.layer?.cornerRadius = 3
        addSubview(dot)

        titleSlot.wantsLayer = false
        addSubview(titleSlot)

        countLabel.alignment = .right
        addSubview(countLabel)

        addSubview(grip)
        onHover = { [weak self] inside in
            self?.hovered = inside
            self?.refreshChrome()
        }
    }

    required init?(coder: NSCoder) { nil }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    weak var parentList: GroupNavListView?

    var reorderHandle: NSRect {
        NSRect(x: bounds.width - handleWidth, y: 0, width: handleWidth, height: bounds.height)
    }

    func apply(
        title: String,
        count: Int,
        dotColor: NSColor?,
        titleColor: NSColor,
        countColor: NSColor,
        renaming: Bool,
        renameTarget: AnyObject?,
        renameAction: Selector?,
        renameId: String
    ) {
        dot.isHidden = dotColor == nil
        if let dotColor {
            dot.layer?.backgroundColor = dotColor.cgColor
        }

        titleSlot.subviews.forEach { $0.removeFromSuperview() }
        if renaming {
            let field = NSTextField(frame: titleSlot.bounds)
            field.stringValue = title
            field.font = UI.font(13, .semibold)
            field.textColor = titleColor
            field.identifier = NSUserInterfaceItemIdentifier(renameId)
            field.target = renameTarget
            field.action = renameAction
            field.cell?.sendsActionOnEndEditing = true
            field.focusRingType = .none
            field.isBordered = false
            field.drawsBackground = false
            titleSlot.addSubview(field)
        } else {
            let weight: NSFont.Weight = selected ? .semibold : .regular
            let label = UI.plain(title, 13, weight, titleColor)
            label.truncates = true
            label.frame = titleSlot.bounds
            label.autoresizingMask = [.width, .height]
            titleSlot.addSubview(label)
        }

        countLabel.text = "\(count)"
        countLabel.font = UI.font(11)
        countLabel.color = countColor
        needsLayout = true
    }

    func showInsert(after: Bool) {
        dropActive = true
        if insertAfter == after, !insertLine.isHidden { return }
        insertAfter = after
        insertLine.isHidden = false
        insertLine.layer?.backgroundColor = accent.cgColor
        needsLayout = true
        layoutSubtreeIfNeeded()
        refreshChrome()
    }

    func clearInsert() {
        dropActive = false
        insertLine.isHidden = true
        refreshChrome()
    }

    override func layout() {
        super.layout()
        let h = bounds.height
        grip.frame = NSRect(x: bounds.width - gripWidth - trailingPad, y: (h - 22) / 2, width: gripWidth, height: 22)
        countLabel.frame = NSRect(x: grip.frame.minX - 4 - countWidth, y: 0, width: countWidth, height: h)

        let dotSize: CGFloat = 6
        var titleX: CGFloat = 8
        if !dot.isHidden {
            dot.frame = NSRect(x: 10, y: (h - dotSize) / 2, width: dotSize, height: dotSize)
            titleX = 24
        }
        let titleW = max(0, countLabel.frame.minX - 4 - titleX)
        titleSlot.frame = NSRect(x: titleX, y: 0, width: titleW, height: h)
        titleSlot.subviews.forEach { $0.frame = titleSlot.bounds }

        let lineY = insertAfter ? h - 2 : 0
        insertLine.frame = NSRect(x: 6, y: lineY, width: bounds.width - 12, height: 2)
    }

    override func resetCursorRects() {
        super.resetCursorRects()
        addCursorRect(reorderHandle, cursor: .openHand)
    }

    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        guard bounds.contains(local) else { return nil }
        if reorderHandle.contains(local) {
            return grip.hitTest(grip.convert(local, from: self)) ?? grip
        }
        for child in subviews.reversed() where child !== insertLine && child !== grip {
            if let hit = child.hitTest(local) { return hit }
        }
        return self
    }

    override func mouseDown(with event: NSEvent) {
        let local = convert(event.locationInWindow, from: nil)
        guard reorderHandle.contains(local) else { return }
        parentList?.trackReorder(from: self, event: event)
    }

    override func mouseUp(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        if parentList?.consumedClickForReorder == true {
            parentList?.consumedClickForReorder = false
            return
        }
        if reorderHandle.contains(point) { return }
        onClick()
    }

    private func refreshChrome() {
        if dropActive {
            layer?.backgroundColor = (selected ? accentSoft : NSColor.clear.withAlphaComponent(0.08)).cgColor
        } else if selected {
            layer?.backgroundColor = accentSoft.cgColor
        } else if hovered {
            layer?.backgroundColor = accentSoft.withAlphaComponent(0.45).cgColor
        } else {
            layer?.backgroundColor = NSColor.clear.cgColor
        }
    }
}

/// 分组列表：用事件循环跟踪鼠标完成排序（无边框窗口里比 NSDraggingSession 更可靠）。
final class GroupNavListView: FlippedView {
    private(set) var rows: [NavGroupRow] = []
    var onReorder: ((String, String, Bool) -> Void)?
    var consumedClickForReorder = false

    private weak var hintRow: NavGroupRow?
    private var hintAfter = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
    }

    required init?(coder: NSCoder) { nil }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    func addRow(_ row: NavGroupRow) {
        row.parentList = self
        rows.append(row)
        addSubview(row)
    }

    func trackReorder(from row: NavGroupRow, event: NSEvent) {
        guard let window = row.window, !row.groupId.isEmpty else { return }
        let sourceId = row.groupId
        row.alphaValue = 0.45
        consumedClickForReorder = true
        var target: (String, Bool)?
        Log.info("开始侧栏分组排序拖动 source=\(sourceId)")

        while true {
            guard let next = window.nextEvent(matching: [.leftMouseDragged, .leftMouseUp]) else { break }
            if next.type == .leftMouseUp { break }
            let point = convert(next.locationInWindow, from: nil)
            if let hit = hitRow(at: point), hit.row.groupId != sourceId {
                showInsertHint(row: hit.row, after: hit.after)
                target = (hit.row.groupId, hit.after)
            } else {
                clearInsertHint()
                target = nil
            }
        }

        row.alphaValue = 1
        clearInsertHint()

        if let target, target.0 != sourceId {
            onReorder?(sourceId, target.0, target.1)
            Log.info("侧栏分组排序完成 \(sourceId) → \(target.0) after=\(target.1)")
        }
    }

    private func hitRow(at point: NSPoint) -> (row: NavGroupRow, after: Bool)? {
        for row in rows where row.frame.contains(point) {
            return (row, point.y > row.frame.midY)
        }
        return nil
    }

    private func showInsertHint(row: NavGroupRow, after: Bool) {
        if hintRow === row, hintAfter == after { return }
        clearInsertHint()
        hintRow = row
        hintAfter = after
        row.showInsert(after: after)
    }

    private func clearInsertHint() {
        hintRow?.clearInsert()
        hintRow = nil
    }
}

private final class NavGroupGripView: NSView {
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        toolTip = "拖动排序"
    }

    required init?(coder: NSCoder) { nil }

    override var isFlipped: Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        return bounds.contains(local) ? self : nil
    }

    override func draw(_ dirtyRect: NSRect) {
        let text = "⋮⋮" as NSString
        let font = NSFont.systemFont(ofSize: 11)
        let size = text.size(withAttributes: [.font: font])
        let rect = NSRect(
            x: (bounds.width - size.width) / 2,
            y: (bounds.height - size.height) / 2,
            width: size.width,
            height: size.height
        )
        text.draw(in: rect, withAttributes: [
            .font: font,
            .foregroundColor: NSColor.secondaryLabelColor
        ])
    }

    override func mouseDown(with event: NSEvent) {
        guard let row = superview as? NavGroupRow else { return }
        row.parentList?.trackReorder(from: row, event: event)
    }
}