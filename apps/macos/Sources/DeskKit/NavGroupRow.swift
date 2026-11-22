import AppKit

extension NSPasteboard.PasteboardType {
    static let deskkitGroup = NSPasteboard.PasteboardType("deskkit-group")
}

/// 侧栏分组行：短按选择，整行长按后拖动排序，右键菜单。
final class NavGroupRow: HitRow {
    var groupId = ""
    var selected = false { didSet { refreshChrome() } }
    var accentSoft: NSColor = .clear
    var accent: NSColor = .systemBlue

    private let insertLine = NSView()
    private let dot = NSView()
    private let titleSlot = NSView()
    private let countLabel = PlainLabel()
    private var insertAfter = false
    private var hovered = false
    private var dropActive = false { didSet { refreshChrome() } }

    private let countWidth: CGFloat = 28
    private let trailingPad: CGFloat = 8

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

        onHover = { [weak self] inside in
            self?.hovered = inside
            self?.refreshChrome()
        }
    }

    required init?(coder: NSCoder) { nil }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    weak var parentList: GroupNavListView?

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
        countLabel.frame = NSRect(x: bounds.width - trailingPad - countWidth, y: 0, width: countWidth, height: h)

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
        addCursorRect(bounds, cursor: .openHand)
    }

    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        guard bounds.contains(local) else { return nil }
        if let field = titleSlot.subviews.compactMap({ $0 as? NSTextField }).first {
            let inField = titleSlot.convert(local, from: self)
            if let hit = field.hitTest(inField) { return hit }
        }
        return self
    }

    override func mouseDown(with event: NSEvent) {
        guard let window, !groupId.isEmpty else { return }
        let start = event.locationInWindow
        let deadline = Date().addingTimeInterval(0.18)
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
            let dx = next.locationInWindow.x - start.x
            let dy = next.locationInWindow.y - start.y
            if (dx * dx + dy * dy) > 100 {
                parentList?.trackReorder(from: self, event: next)
                return
            }
        }
        parentList?.trackReorder(from: self, event: event)
    }

    override func mouseUp(with event: NSEvent) {}

    private func refreshChrome() {
        if dropActive {
            layer?.backgroundColor = accent.withAlphaComponent(0.28).cgColor
        } else if selected {
            layer?.backgroundColor = accent.withAlphaComponent(0.18).cgColor
        } else if hovered {
            layer?.backgroundColor = accent.withAlphaComponent(0.09).cgColor
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
        row.alphaValue = 0.55
        consumedClickForReorder = true
        var target: (String, Bool)?
        Log.info("开始侧栏分组排序拖动 source=\(sourceId)")
        follow(event, sourceId: sourceId, target: &target)

        while true {
            guard let next = window.nextEvent(matching: [.leftMouseDragged, .leftMouseUp]) else { break }
            if next.type == .leftMouseUp { break }
            follow(next, sourceId: sourceId, target: &target)
        }

        row.alphaValue = 1
        clearInsertHint()

        if let target, target.0 != sourceId {
            onReorder?(sourceId, target.0, target.1)
            Log.info("侧栏分组排序完成 \(sourceId) → \(target.0) after=\(target.1)")
        }
    }

    private func follow(_ event: NSEvent, sourceId: String, target: inout (String, Bool)?) {
        autoScroll(toward: event)
        let point = convert(event.locationInWindow, from: nil)
        if let hit = nearestRow(at: point), hit.row.groupId != sourceId {
            showInsertHint(row: hit.row, after: hit.after)
            target = (hit.row.groupId, hit.after)
        }
    }

    private func nearestRow(at point: NSPoint) -> (row: NavGroupRow, after: Bool)? {
        var nearest: NavGroupRow?
        var best = CGFloat.greatestFiniteMagnitude
        var after = false
        for row in rows {
            let distance = abs(point.y - row.frame.midY)
            if distance < best {
                best = distance
                nearest = row
                after = point.y > row.frame.midY
            }
        }
        guard let nearest else { return nil }
        return (nearest, after)
    }

    private func autoScroll(toward event: NSEvent) {
        guard let scroll = enclosingScrollView else { return }
        let visible = scroll.contentView.bounds
        let local = scroll.contentView.convert(event.locationInWindow, from: nil)
        let edge: CGFloat = 28
        var origin = visible.origin
        if local.y < visible.minY + edge {
            origin.y = max(0, origin.y - 16)
        } else if local.y > visible.maxY - edge {
            let limit = max(0, bounds.height - visible.height)
            origin.y = min(limit, origin.y + 16)
        } else {
            return
        }
        scroll.contentView.scroll(to: origin)
        scroll.reflectScrolledClipView(scroll.contentView)
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
