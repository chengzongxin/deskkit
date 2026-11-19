import AppKit
import ApplicationServices
import DeskKitCore
import ServiceManagement
import UniformTypeIdentifiers

/// 按原型 A/B/C/E/F 还原的 AppKit 壳层。尺寸与色值全部来自 contracts。
final class MenuChoice: NSObject {
    let value: String
    let run: () -> Void
    init(_ value: String, _ run: @escaping (String) -> Void) {
        self.value = value
        self.run = { run(value) }
    }
}

final class HitBox: NSObject {
    let hit: Hit
    init(_ hit: Hit) { self.hit = hit }
}

final class Shell: NSObject, NSTextFieldDelegate, NSWindowDelegate, NSMenuDelegate {
    let session: Session

    private let launcher = KeyPanel(
        contentRect: NSRect(x: 0, y: 0, width: 800, height: 360),
        styleMask: [.borderless, .fullSizeContentView],
        backing: .buffered,
        defer: false
    )
    private let launcherRoot = FlippedView()
    private let searchIcon = IconView()
    private let capsuleSlot = FlippedView()
    private let field = NSTextField()
    private let clearButton = NSButton()
    private let bodyScroll = NSScrollView()
    private let body = FlippedView()
    private let footerBar = FlippedView()
    private let footerHints = FlippedView()
    private let footerLeft = UI.label("", 11, .medium, .tertiaryLabelColor)
    private let footerBrand = UI.label("DeskKit", 11, .semibold, .tertiaryLabelColor)

    private var toolWindow: NSWindow?
    private var groupsWindow: NSWindow?
    private var settingsWindow: NSWindow?
    private var guideWindow: NSWindow?

    private var settingsTab = "general"
    private var guideStep = 1
    private var panelNav = "all"
    private var panelEdit = false
    private var renamingGroupId: String?
    private var renamingCommit = false
    private var panelSearch = ""
    private var foldedGroups: Set<String> = []
    private var recordingHotkey: String?
    var onHotkeyChanged: () -> Void = {}
    private var activeTool = ""
    private var toolMode = "grid"
    private var blurWork: DispatchWorkItem?
    private var lastToolOutput = ""
    private var hotkeyReady = false
    private var homeList: [Hit] = []
    private var listScrollKey = ""
    private var appearanceWatch: NSKeyValueObservation?
    private var backgroundImage: NSImage?
    private var backgroundLoadedPath = ""
    private var pendingCopy: String?
    private var actionMenu: NSMenu?
    private var dropHighlight: NSView?
    var onAskHotkeyAccess: () -> Void = {}

    init(session: Session) {
        self.session = session
        super.init()
        configureLauncher()
        buildLauncherChrome()
        appearanceWatch = NSApp.observe(\.effectiveAppearance, options: [.new]) { [weak self] _, _ in
            DispatchQueue.main.async {
                guard let self, self.session.settings.theme == "system" else { return }
                self.refreshOpenSurfaces()
            }
        }
        NotificationCenter.default.addObserver(self, selector: #selector(launcherResigned), name: NSWindow.didResignKeyNotification, object: launcher)
        NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self else { return event }
            if self.recordingHotkey != nil {
                self.captureHotkey(event)
                return nil
            }
            if self.launcher.isKeyWindow { return self.handleLauncherKey(event) ? nil : event }
            if self.toolWindow?.isKeyWindow == true { return self.handleToolKey(event) ? nil : event }
            if self.settingsWindow?.isKeyWindow == true, event.keyCode == 53 {
                Log.info("Esc 关闭设置")
                self.closeSettings()
                return nil
            }
            if self.groupsWindow?.isKeyWindow == true, event.keyCode == 53 {
                if self.renamingGroupId != nil {
                    self.renamingGroupId = nil
                    Log.info("Esc 取消重命名分组")
                    self.renderGroups()
                    return nil
                }
                Log.info("Esc 关闭快捷启动")
                self.closeGroups()
                return nil
            }
            return event
        }
    }

    // MARK: - Public

    func showLauncher() {
        blurWork?.cancel()
        rebuildLauncher()
        place(launcher, topRatio: session.store.layout("launcher", "topRatio"))
        launcher.orderFrontRegardless()
        NSApp.activate(ignoringOtherApps: true)
        launcher.makeKeyAndOrderFront(nil)
        focusSearchField(reason: "呼出")
        // 激活时机晚一拍时再抢一次焦点，避免看起来能呼出却打不了字
        DispatchQueue.main.async { [weak self] in
            self?.focusSearchField(reason: "呼出延迟")
        }
        Log.info("浮层 \(Int(launcher.frame.width))x\(Int(launcher.frame.height)) 状态 \(session.machine.state)")
    }

    private func focusSearchField(reason: String) {
        field.isEditable = true
        field.isSelectable = true
        field.isEnabled = true
        let ok = launcher.makeFirstResponder(field)
        let key = launcher.isKeyWindow
        let first = launcher.firstResponder === field || launcher.firstResponder === field.currentEditor()
        Log.info("输入框聚焦[\(reason)] key=\(key) first=\(first) makeFirstResponder=\(ok)")
    }

    func hideLauncher() {
        blurWork?.cancel()
        launcher.orderOut(nil)
    }

    func noteHotkey(_ ready: Bool) {
        guard hotkeyReady != ready else { return }
        hotkeyReady = ready
        Log.info(ready ? "呼出键已生效" : "呼出键未生效：辅助功能未授予当前 DeskKit")
        if launcher.isVisible { rebuildLauncher() }
    }

    func showTools() { openToolGrid() }
    func showTool(_ id: String) { openToolPage(id) }
    func showGroups() { renderGroups() }

    func toggleGroups() {
        if groupsWindow?.isVisible == true {
            dismissGroups()
            Log.info("热键隐藏快捷启动")
            return
        }
        Log.info("热键唤出快捷启动")
        showGroups()
    }

    func panelHotkeyText() -> String { hotkeyText("panel") }
    func showSettings() { renderSettings() }
    func showGuide() {
        guideStep = 1
        renderGuide()
    }

    func captureAll(to dir: URL) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        session.query = ""
        session.triggerWord = ""
        session.hits = []
        _ = session.machine.transition("HIDDEN")
        _ = session.machine.transition("A1")
        showLauncher()
        snap("A1-home", launcher, to: dir)

        session.query = "wx"
        session.search()
        rebuildLauncher()
        snap("A3-results", launcher, to: dir)

        session.query = "zzzznope"
        session.search()
        rebuildLauncher()
        snap("A4-empty", launcher, to: dir)

        session.query = "fy hello"
        session.search()
        rebuildLauncher()
        snap("A-trigger", launcher, to: dir)

        renderGroups()
        if let w = groupsWindow { snap("B-panel", w, to: dir) }

        openToolGrid()
        if let w = toolWindow { snap("C-grid", w, to: dir) }

        openToolPage("calc")
        if let w = toolWindow { snap("C-calc", w, to: dir) }

        settingsTab = "general"
        renderSettings()
        if let w = settingsWindow { snap("E-general", w, to: dir) }
        settingsTab = "hotkeys"
        renderSettings()
        if let w = settingsWindow { snap("E-hotkeys", w, to: dir) }
        settingsTab = "about"
        renderSettings()
        if let w = settingsWindow { snap("E-about", w, to: dir) }

        guideStep = 1
        renderGuide()
        if let w = guideWindow { snap("F-step1", w, to: dir) }
        guideStep = 2
        renderGuide()
        if let w = guideWindow { snap("F-step2", w, to: dir) }
        guideStep = 3
        renderGuide()
        if let w = guideWindow { snap("F-step3", w, to: dir) }

        Log.info("验证截图已写入 \(dir.path)")
    }

    // MARK: - Launcher A

    private func configureLauncher() {
        launcher.level = .floating
        launcher.isFloatingPanel = true
        launcher.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        launcher.hidesOnDeactivate = false
        launcher.hasShadow = true
        launcher.isOpaque = false
        launcher.backgroundColor = .clear
        launcher.appearance = nil
        launcher.delegate = self
        let width = session.store.layout("launcher", "width")
        launcher.contentMinSize = NSSize(width: width, height: session.store.layout("launcher", "emptyHeight"))
        launcher.contentMaxSize = NSSize(width: width, height: session.store.layout("launcher", "maxHeight"))
    }

    private func buildLauncherChrome() {
        let width = session.store.layout("launcher", "width")
        let height = session.store.layout("launcher", "defaultHeight")
        let root = FlippedView(frame: NSRect(x: 0, y: 0, width: width, height: height))
        root.wantsLayer = true
        root.layer?.backgroundColor = color("bg.float").cgColor
        root.layer?.cornerRadius = session.store.layout("launcher", "cornerRadius")
        root.layer?.borderWidth = 1
        root.layer?.borderColor = color("border.default").cgColor
        root.layer?.masksToBounds = true
        launcher.contentView = root
        applyBackground(to: root)

        launcherRoot.frame = root.bounds
        root.addSubview(launcherRoot)

        let inputRow: CGFloat = 64
        func mid(_ height: CGFloat) -> CGFloat { floor((inputRow - height) / 2) }

        searchIcon.image = AppIcons.symbol("magnifyingglass", size: 16, tint: color("text.tertiary"))
        searchIcon.frame = NSRect(x: 16, y: mid(20), width: 20, height: 20)
        launcherRoot.addSubview(searchIcon)

        capsuleSlot.frame = NSRect(x: 44, y: mid(28), width: 0, height: 28)
        launcherRoot.addSubview(capsuleSlot)

        let cell = CenteredFieldCell(textCell: "")
        cell.isEditable = true
        cell.isSelectable = true
        cell.isBordered = false
        cell.drawsBackground = false
        cell.font = UI.font(session.store.layout("type", "input", "size"), .medium)
        cell.textColor = color("text.primary")
        cell.focusRingType = .none
        field.cell = cell
        field.isBezeled = false
        field.isBordered = false
        field.drawsBackground = false
        field.isEditable = true
        field.isSelectable = true
        field.isEnabled = true
        field.focusRingType = .none
        field.font = cell.font
        field.textColor = color("text.primary")
        field.delegate = self
        field.frame = NSRect(x: 44, y: mid(36), width: 400, height: 36)
        field.placeholderAttributedString = NSAttributedString(
            string: session.store.text("launcher.placeholder"),
            attributes: [.font: field.font as Any, .foregroundColor: color("text.tertiary")]
        )
        launcherRoot.addSubview(field)

        clearButton.title = "✕"
        clearButton.isBordered = false
        clearButton.font = UI.font(12, .medium)
        clearButton.contentTintColor = color("text.tertiary")
        clearButton.target = self
        clearButton.action = #selector(clearQuery)
        clearButton.frame = NSRect(x: 500, y: mid(28), width: 28, height: 28)
        clearButton.isHidden = true
        launcherRoot.addSubview(clearButton)

        let gear = symbolButton("gearshape", #selector(showSettingsFromLauncher))
        gear.identifier = NSUserInterfaceItemIdentifier("chrome-gear")
        gear.frame = NSRect(x: 536, y: mid(28), width: 28, height: 28)
        launcherRoot.addSubview(gear)

        let close = symbolButton("xmark", #selector(hideFromButton))
        close.identifier = NSUserInterfaceItemIdentifier("chrome-close")
        close.frame = NSRect(x: 572, y: mid(28), width: 28, height: 28)
        launcherRoot.addSubview(close)

        let divider = NSView(frame: NSRect(x: 0, y: 63, width: width, height: 1))
        divider.wantsLayer = true
        divider.layer?.backgroundColor = color("border.default").cgColor
        divider.identifier = NSUserInterfaceItemIdentifier("chrome-divider")
        launcherRoot.addSubview(divider)

        bodyScroll.drawsBackground = false
        bodyScroll.hasVerticalScroller = true
        bodyScroll.autohidesScrollers = true
        bodyScroll.scrollerStyle = .overlay
        bodyScroll.borderType = .noBorder
        bodyScroll.automaticallyAdjustsContentInsets = false
        bodyScroll.scrollerInsets = NSEdgeInsets(top: 6, left: 0, bottom: 6, right: 10)
        bodyScroll.clipsToBounds = true
        bodyScroll.contentView.clipsToBounds = true
        bodyScroll.documentView = body
        bodyScroll.frame = NSRect(x: 0, y: 64, width: width, height: 264)
        launcherRoot.addSubview(bodyScroll)
        launcherRoot.clipsToBounds = true

        footerBar.wantsLayer = true
        footerBar.layer?.backgroundColor = color("bg.subtle").cgColor
        footerBar.frame = NSRect(x: 0, y: height - 32, width: width, height: 32)
        launcherRoot.addSubview(footerBar)
        let footLine = NSView(frame: NSRect(x: 0, y: 0, width: width, height: 1))
        footLine.wantsLayer = true
        footLine.layer?.backgroundColor = color("border.default").cgColor
        footerBar.addSubview(footLine)
        footerHints.frame = NSRect(x: 12, y: 0, width: 500, height: 32)
        footerBar.addSubview(footerHints)
        footerLeft.frame = NSRect(x: 16, y: 8, width: 520, height: 16)
        footerLeft.isHidden = true
        footerBrand.frame = NSRect(x: width - 100, y: 8, width: 84, height: 16)
        footerBrand.alignment = .right
        footerBar.addSubview(footerLeft)
        footerBar.addSubview(footerBrand)
        let footHit = HitRow(frame: NSRect(x: 0, y: 0, width: 520, height: 32))
        footHit.identifier = NSUserInterfaceItemIdentifier("hotkey-foot")
        footHit.onClick = { [weak self] in
            guard let self, !self.hotkeyReady else { return }
            self.onAskHotkeyAccess()
        }
        footerBar.addSubview(footHit)
        layoutLauncherChrome()
    }

    func rebuildLauncher() {
        let width = session.store.layout("launcher", "width")
        let triggered = !session.triggerWord.isEmpty
        let raw = triggered ? session.payload : session.query
        layoutLauncherChrome()

        capsuleSlot.subviews.forEach { $0.removeFromSuperview() }
        if triggered {
            let pill = makeCapsule(session.triggerWord)
            capsuleSlot.frame = NSRect(x: 44, y: 18, width: pill.frame.width, height: 28)
            capsuleSlot.addSubview(pill)
            let fieldX = capsuleSlot.frame.maxX + 8
            field.frame = NSRect(x: fieldX, y: 14, width: max(80, clearButton.frame.minX - 8 - fieldX), height: 36)
        } else {
            capsuleSlot.frame = NSRect(x: 44, y: 18, width: 0, height: 28)
            field.frame = NSRect(x: 44, y: 14, width: max(80, clearButton.frame.minX - 8 - 44), height: 36)
        }
        if field.stringValue != raw { field.stringValue = raw }
        let prompt = triggered
            ? (session.resolveTrigger(session.triggerWord)?.placeholder ?? "")
            : session.store.text("launcher.placeholder")
        field.placeholderAttributedString = NSAttributedString(
            string: prompt,
            attributes: [.font: field.font as Any, .foregroundColor: color("text.tertiary")]
        )
        clearButton.isHidden = field.stringValue.isEmpty && session.triggerWord.isEmpty

        layoutFooter()

        body.subviews.forEach { $0.removeFromSuperview() }
        var contentH: CGFloat = 8
        if triggered {
            contentH = drawTriggerExclusive(contentH)
        } else if session.query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            contentH = drawHome(contentH)
        } else if session.hits.isEmpty || session.machine.state == "A4" {
            contentH = drawEmpty(contentH)
        } else {
            contentH = drawResults(contentH)
        }

        let home = session.query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && session.triggerWord.isEmpty
        let empty = session.machine.state == "A4" || (session.hits.isEmpty && !home && !triggered)
        let minH: CGFloat = home
            ? session.store.layout("launcher", "defaultHeight")
            : (empty ? session.store.layout("launcher", "emptyHeight") : 220)
        let height = min(session.store.layout("launcher", "maxHeight"), max(minH, 64 + contentH + 40))

        var frame = launcher.frame
        let top = frame.maxY
        frame.size = NSSize(width: width, height: height)
        frame.origin.y = top - height
        launcher.setFrame(frame, display: true)
        launcherRoot.frame = NSRect(x: 0, y: 0, width: width, height: height)
        body.frame = NSRect(x: 0, y: 0, width: width, height: max(contentH, 1))
        bodyScroll.frame = NSRect(x: 0, y: 64, width: width, height: max(1, height - 96))
        bodyScroll.scrollerInsets = NSEdgeInsets(top: 6, left: 0, bottom: 6, right: 10)
        let viewport = bodyScroll.frame.height
        let scrollKey = session.query + "|" + session.triggerWord
        if scrollKey != listScrollKey {
            listScrollKey = scrollKey
            bodyScroll.contentView.scroll(to: .zero)
            if contentH > viewport {
                Log.info("列表可滚动，内容 \(Int(contentH))，可视 \(Int(viewport))")
            }
        }
        footerBar.frame = NSRect(x: 0, y: height - 32, width: width, height: 32)
        if let divider = launcherRoot.subviews.first(where: { $0.identifier?.rawValue == "chrome-divider" }) {
            divider.frame = NSRect(x: 0, y: 63, width: width, height: 1)
            divider.layer?.backgroundColor = color("border.default").cgColor
        }
        applyBackground(to: launcher.contentView)
        if launcher.isVisible, launcher.isKeyWindow {
            let editing = launcher.firstResponder === field || launcher.firstResponder === field.currentEditor()
            if !editing {
                _ = launcher.makeFirstResponder(field)
            }
        }
    }

    private func makeCapsule(_ word: String) -> NSView {
        let pill = FlippedView(frame: NSRect(x: 0, y: 0, width: 72, height: 28))
        pill.wantsLayer = true
        pill.layer?.backgroundColor = color("accent.soft").cgColor
        pill.layer?.cornerRadius = 14
        pill.layer?.borderWidth = 1
        pill.layer?.borderColor = color("accent").cgColor
        let text = UI.label(word, 12, .semibold, color("accent.hover"))
        text.frame = NSRect(x: 10, y: 5, width: 36, height: 18)
        pill.addSubview(text)
        let x = NSButton(title: "✕", target: self, action: #selector(dropTrigger))
        x.isBordered = false
        x.font = UI.font(9, .bold)
        x.wantsLayer = true
        x.layer?.backgroundColor = color("accent").cgColor
        x.layer?.cornerRadius = 7
        x.attributedTitle = NSAttributedString(string: "✕", attributes: [.foregroundColor: NSColor.white, .font: UI.font(9, .bold)])
        x.frame = NSRect(x: 48, y: 7, width: 14, height: 14)
        pill.addSubview(x)
        text.sizeToFit()
        let w = max(72, text.frame.width + 36)
        pill.frame.size.width = w
        x.frame.origin.x = w - 20
        return pill
    }

    private func drawHome(_ start: CGFloat) -> CGFloat {
        var y = start
        let rows = session.recentHits()
        let seen = Set(rows.map(\.id))
        let candidates = Array(session.index.filter { $0.kind == "app" && !seen.contains($0.id) }.prefix(12))
        homeList = rows + candidates
        var number = 1
        if !rows.isEmpty {
            y = section(y, "最近使用", "\(rows.count) 条")
            for hit in rows {
                y = drawRow(y, hit, number, false, showEnter: false)
                number += 1
            }
        }
        if !candidates.isEmpty {
            y = section(y + 4, "候选", "\(candidates.count) 条")
            for hit in candidates {
                y = drawRow(y, hit, number, false, showEnter: false)
                number += 1
            }
        }

        y = section(y + 4, "推荐工具", "")
        y = drawChips(y, [
            ("▤ 剪贴板历史", #selector(chipClipboard)),
            ("= 计算器", #selector(chipCalc)),
            ("译 翻译", #selector(chipTranslate))
        ])

        y = section(y + 4, "试试这些输入（拼音 / 首字母 / 算式 / 触发词）", "")
        let demos = ["wx", "kzmb", "word", "fy hello", "123*4", "#3B6CF6", "100cny to usd", "color #3B6CF6"]
        y = drawDemoChips(y, demos)
        return y + 8
    }

    private func drawResults(_ start: CGFloat) -> CGFloat {
        var y = start
        let labels = ["tool": "内置工具", "app": "应用", "file": "文件", "url": "网址"]
        var shown = 0
        let selected = session.machine.state == "A3" || !session.hits.isEmpty
        for kind in ["tool", "app", "file", "url"] {
            let rows = session.hits.filter { $0.kind == kind }
            guard !rows.isEmpty else { continue }
            y = section(y, labels[kind] ?? kind, "\(rows.count) 条")
            for hit in rows {
                let isSel = shown == session.selection
                shown += 1
                y = drawRow(y, hit, shown, isSel && selected, showEnter: isSel && selected)
            }
        }
        return y + 8
    }

    private func drawTriggerExclusive(_ start: CGFloat) -> CGFloat {
        var y = start
        let word = session.triggerWord
        let payload = session.payload
        let tr = session.resolveTrigger(word)
        let layout = launcherLayout()
        let head = FlippedView(frame: NSRect(x: layout.edge, y: y, width: layout.rowW, height: 32))
        let lock = UI.label("触发词 \(word) · 工具独占视图", 12, .semibold, color("text.secondary"))
        lock.frame = NSRect(x: 0, y: 6, width: 320, height: 18)
        head.addSubview(lock)
        if let label = tr?.label {
            let tag = pillTag(label)
            tag.frame = NSRect(x: 330, y: 4, width: 72, height: 22)
            head.addSubview(tag)
        }
        let exit = softButton("退出胶囊", #selector(dropTrigger))
        exit.frame = NSRect(x: layout.pad + layout.rowW - 100, y: 2, width: 100, height: 28)
        head.addSubview(exit)
        body.addSubview(head)
        y += 40

        let card = FlippedView(frame: NSRect(x: layout.edge, y: y, width: layout.rowW, height: 180))
        card.wantsLayer = true
        card.layer?.backgroundColor = color("bg.float").cgColor
        card.layer?.cornerRadius = 10
        card.layer?.borderWidth = 1
        card.layer?.borderColor = color("border.default").cgColor

        let toolId = tr?.toolId ?? ""
        let hit = session.hits.first
        switch toolId {
        case "translate":
            addExcl(card, "原文", payload.isEmpty ? "（输入待译文本）" : payload, hit?.title ?? "—", hit?.subtitle ?? "")
        case "calc":
            addExcl(card, "表达式 / 单位换算", payload.isEmpty ? "（输入算式）" : payload, hit?.title ?? "—", hit?.subtitle ?? "支持四则运算与单位换算")
        case "color":
            addColorExcl(card, payload, hit?.title ?? "")
        case "timestamp":
            addExcl(card, "时间戳 ↔ 日期双向", payload.isEmpty ? "（输入时间戳或日期）" : payload, hit?.title ?? "—", "时区：系统当前时区")
        case "clipboard":
            addClipExcl(card, payload)
        default:
            addExcl(card, "工具", payload, hit?.title ?? "—", "")
        }
        body.addSubview(card)
        y += 196

        var chips: [(String, Selector)] = []
        if !toolId.isEmpty {
            chips.append(("打开「\(toolTitle(toolId))」工具内页", #selector(openTriggerTool)))
        }
        chips.append(("Enter 复制结果", #selector(copyTriggerResult)))
        chips.append(("Esc 退出触发词胶囊", #selector(dropTrigger)))
        y = drawChips(y, chips)
        return y + 8
    }

    private func addExcl(_ card: NSView, _ sub: String, _ big: String, _ result: String, _ note: String) {
        let s1 = UI.label(sub, 12, .regular, color("text.tertiary"))
        s1.frame = NSRect(x: 16, y: 14, width: 640, height: 16)
        let b1 = UI.label(big, 20, .semibold, color("text.primary"))
        b1.frame = NSRect(x: 16, y: 36, width: 640, height: 28)
        let s2 = UI.label("结果", 12, .regular, color("text.tertiary"))
        s2.frame = NSRect(x: 16, y: 78, width: 640, height: 16)
        let b2 = UI.label(result, 22, .semibold, color("accent.hover"))
        b2.frame = NSRect(x: 16, y: 100, width: 640, height: 32)
        let n = UI.label(note, 12, .regular, color("text.tertiary"))
        n.frame = NSRect(x: 16, y: 144, width: 640, height: 16)
        [s1, b1, s2, b2, n].forEach { card.addSubview($0) }
    }

    private func addColorExcl(_ card: NSView, _ payload: String, _ result: String) {
        let s = UI.label("HEX ↔ RGB ↔ HSL · 输入 \(payload.isEmpty ? "#RRGGBB" : payload)", 12, .regular, color("text.tertiary"))
        s.frame = NSRect(x: 16, y: 14, width: 640, height: 16)
        card.addSubview(s)
        let swatch = NSView(frame: NSRect(x: 16, y: 40, width: 656, height: 56))
        swatch.wantsLayer = true
        swatch.layer?.cornerRadius = 8
        swatch.layer?.borderWidth = 1
        swatch.layer?.borderColor = color("border.default").cgColor
        let hex = colorHex(from: payload) ?? session.store.color("accent")
        swatch.layer?.backgroundColor = Theme.paint(hex).cgColor
        card.addSubview(swatch)
        let out = UI.label(result.isEmpty ? "格式非法" : result, 13, .medium, color("text.primary"))
        out.frame = NSRect(x: 16, y: 110, width: 640, height: 40)
        out.maximumNumberOfLines = 2
        card.addSubview(out)
    }

    private func addClipExcl(_ card: NSView, _ payload: String) {
        let needle = payload.lowercased()
        let list = session.clips.filter { needle.isEmpty || $0.text.lowercased().contains(needle) }
        let s = UI.label("剪贴板历史 · 点击复用（\(list.count) 条）", 12, .regular, color("text.tertiary"))
        s.frame = NSRect(x: 16, y: 12, width: 640, height: 16)
        card.addSubview(s)
        var y: CGFloat = 36
        if list.isEmpty {
            let empty = UI.label("还没有复制记录，复制任意内容后这里会出现", 12, .regular, color("text.secondary"))
            empty.frame = NSRect(x: 16, y: y, width: 640, height: 18)
            card.addSubview(empty)
            return
        }
        for clip in list.prefix(5) {
            let row = HitRow(frame: NSRect(x: 16, y: y, width: 656, height: 28))
            row.wantsLayer = true
            row.layer?.backgroundColor = color("bg.subtle").cgColor
            row.layer?.cornerRadius = 8
            let t = UI.label(String(clip.text.prefix(48)), 12, .regular, color("text.primary"))
            t.frame = NSRect(x: 10, y: 5, width: 620, height: 18)
            row.addSubview(t)
            let text = clip.text
            row.onClick = { [weak self] in
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(text, forType: .string)
                Log.info("已复用剪贴板记录")
                self?.session.hide()
            }
            card.addSubview(row)
            y += 32
        }
    }

    private func drawEmpty(_ start: CGFloat) -> CGFloat {
        let layout = launcherLayout()
        let w = layout.width
        let ico = NSView(frame: NSRect(x: (w - 56) / 2, y: start + 24, width: 56, height: 56))
        ico.wantsLayer = true
        ico.layer?.cornerRadius = 28
        ico.layer?.backgroundColor = color("bg.subtle").cgColor
        let mark = AppIcons.view(
            image: AppIcons.symbol("magnifyingglass", size: 22, tint: color("text.tertiary")),
            frame: NSRect(x: 16, y: 16, width: 24, height: 24)
        )
        ico.addSubview(mark)
        body.addSubview(ico)

        let q = session.query
        let title = UI.label("没有找到与 “\(q)” 相关的内容", 13, .regular, color("text.secondary"))
        title.alignment = .center
        title.frame = NSRect(x: layout.pad, y: start + 92, width: layout.rowW, height: 22)
        body.addSubview(title)

        let actions: [(String, Selector, Bool)] = [
            ("用浏览器搜索", #selector(browse), true),
            ("清空重来", #selector(clearQuery), false),
            ("查看全部工具", #selector(showToolsFromLauncher), false)
        ]
        let totalW: CGFloat = 130 * 3 + 10 * 2
        var x: CGFloat = (w - totalW) / 2
        for action in actions {
            let button = action.2 ? primaryButton(action.0, action.1) : softButton(action.0, action.1)
            button.frame = NSRect(x: x, y: start + 128, width: 130, height: 30)
            body.addSubview(button)
            x += 140
        }
        let tip = UI.label("\(session.store.formatChord(platform: "macos", action: "escape")) 逐级回退 · 也可以试试 wx / kzmb / 123*4", 11, .regular, color("text.tertiary"))
        tip.alignment = .center
        tip.frame = NSRect(x: layout.pad, y: start + 172, width: layout.rowW, height: 16)
        body.addSubview(tip)
        return start + 210
    }

    private func section(_ y: CGFloat, _ title: String, _ count: String) -> CGFloat {
        let layout = launcherLayout()
        let left = UI.label(title, 12, .semibold, color("text.tertiary"))
        left.frame = NSRect(x: layout.edge, y: y, width: layout.rowW - 100, height: 22)
        body.addSubview(left)
        if !count.isEmpty {
            let right = UI.label(count, 12, .regular, color("text.tertiary"))
            right.frame = NSRect(x: layout.pad + layout.rowW - 80 - 12, y: y, width: 80, height: 22)
            right.alignment = .right
            body.addSubview(right)
        }
        return y + 26
    }

    private func drawRow(_ y: CGFloat, _ hit: Hit, _ index: Int, _ selected: Bool, showEnter: Bool) -> CGFloat {
        let layout = launcherLayout()
        let rowHeight: CGFloat = session.settings.density == "compact" ? 40 : 48
        let row = HitRow(frame: NSRect(x: layout.pad, y: y, width: layout.rowW, height: rowHeight))
        row.swallowsClicks = true
        row.wantsLayer = true
        row.layer?.backgroundColor = (selected ? color("bg.selected") : NSColor.clear).cgColor
        row.layer?.cornerRadius = 8
        if selected {
            let bar = NSView(frame: NSRect(x: 0, y: 0, width: 3, height: rowHeight))
            bar.wantsLayer = true
            bar.layer?.backgroundColor = color("accent").cgColor
            row.addSubview(bar)
        }
        let icon = AppIcons.view(
            image: AppIcons.image(for: hit, size: 32, tint: color("accent")),
            frame: NSRect(x: 12, y: 8, width: 32, height: 32)
        )
        row.addSubview(icon)
        let title = UI.label(hit.title, 14, .medium, color("text.primary"))
        title.frame = NSRect(x: 54, y: 6, width: 460, height: 18)
        let sub = UI.label(hit.subtitle, 12, .regular, color("text.tertiary"))
        sub.frame = NSRect(x: 54, y: 26, width: 460, height: 16)
        row.addSubview(title)
        row.addSubview(sub)
        let alt = altLabel()
        let badge = index <= 9 ? "\(alt)\(index)" : "\(index)"
        let badgeW = KeycapLabel.width(for: badge)
        let badgeX = layout.rowW - badgeW - 12
        let number = KeycapLabel(frame: NSRect(x: badgeX, y: 14, width: badgeW, height: 20))
        number.text = badge
        number.tint = selected ? color("accent.hover") : color("text.secondary")
        number.border = selected ? color("accent") : color("border.default")
        number.fill = color("bg.input")
        number.toolTip = "按住 \(alt) 再按 \(index) 打开"
        row.addSubview(number)
        if showEnter {
            let enter = UI.label(session.store.keyLabel(platform: "macos", key: "enter"), 12, .regular, color("text.tertiary"))
            enter.frame = NSRect(x: number.frame.minX - 22, y: 14, width: 18, height: 20)
            row.addSubview(enter)
        }
        let textRight = (showEnter ? number.frame.minX - 26 : number.frame.minX) - 12
        title.frame.size.width = textRight - title.frame.minX
        sub.frame.size.width = textRight - sub.frame.minX
        row.onClick = { [weak self] in self?.session.execute(hit: hit) }
        row.onRightClick = { [weak self] event in
            self?.showItemMenu(hit, event, in: row)
        }
        body.addSubview(row)
        return y + rowHeight
    }

    private func chipWidth(_ title: String, floor: CGFloat) -> CGFloat {
        let size = (title as NSString).size(withAttributes: [.font: UI.font(12, .medium)])
        return min(220, max(floor, ceil(size.width) + 22))
    }

    private func drawChips(_ y: CGFloat, _ items: [(String, Selector)]) -> CGFloat {
        let layout = launcherLayout()
        var x: CGFloat = layout.edge
        var rowY = y
        let wrap = layout.pad + layout.rowW
        for item in items {
            let chip = softButton(item.0, item.1)
            let w = chipWidth(item.0, floor: 72)
            if x + w > wrap {
                x = layout.edge
                rowY += 34
            }
            chip.frame = NSRect(x: x, y: rowY, width: w, height: 28)
            body.addSubview(chip)
            x += w + 8
        }
        return rowY + 34
    }

    private func drawDemoChips(_ y: CGFloat, _ demos: [String]) -> CGFloat {
        let layout = launcherLayout()
        var x: CGFloat = layout.edge
        var rowY = y
        let wrap = layout.pad + layout.rowW
        for demo in demos {
            let chip = softButton(demo, #selector(demoQuery(_:)))
            chip.identifier = NSUserInterfaceItemIdentifier(demo)
            let w = chipWidth(demo, floor: 44)
            if x + w > wrap {
                x = layout.edge
                rowY += 34
            }
            chip.frame = NSRect(x: x, y: rowY, width: w, height: 28)
            body.addSubview(chip)
            x += w + 8
        }
        return rowY + 34
    }

    // MARK: - Panel B

    private func renderGroups() {
        if panelNav == "fav" { panelNav = "all" }
        dropHighlight = nil
        let preset = NSSize(width: session.store.layout("windows", "panel", "width"), height: session.store.layout("windows", "panel", "height"))
        let window = groupsWindow ?? makeWindow("DeskKit 快捷启动", preset)
        window.minSize = NSSize(width: 760, height: 480)
        window.contentMinSize = window.minSize
        let opening = !window.isVisible
        let liveFrame = opening ? nil : window.frame
        if opening, session.settings.panelPlacedByUser {
            applySavedPanelFrame(window)
        }
        let size = (liveFrame?.size ?? window.frame.size)
        // 完全透明的窗口收不到访达拖放。外层画出圆角底，并留一点点不透明，拖拽才能命中。
        let drop = PanelDropView(frame: NSRect(origin: .zero, size: size))
        drop.fill = color("bg.canvas")
        let root = FlippedView(frame: drop.bounds)
        root.autoresizingMask = [.width, .height]
        drop.addSubview(root)
        window.contentView = drop
        window.backgroundColor = color("bg.canvas").withAlphaComponent(0.05)
        window.isOpaque = false
        paint(root, "bg.canvas")
        card(root)

        let headerH: CGFloat = 48
        let footerH: CGFloat = 36
        let edge = uiEdge()
        let gap: CGFloat = 8
        let controlH: CGFloat = 28
        let controlY = (headerH - controlH) / 2
        let header = WindowDragView(frame: NSRect(x: 0, y: 0, width: size.width, height: headerH))
        paint(header, "bg.float")
        var right = size.width - edge
        let close = symbolButton("xmark", #selector(closeGroups))
        right -= controlH
        close.frame = NSRect(x: right, y: controlY, width: controlH, height: controlH)
        close.autoresizingMask = [.minXMargin]
        header.addSubview(close)
        let gear = symbolButton("gearshape", #selector(showSettingsFromLauncher))
        right -= gap + controlH
        gear.frame = NSRect(x: right, y: controlY, width: controlH, height: controlH)
        gear.autoresizingMask = [.minXMargin]
        header.addSubview(gear)
        let searchW: CGFloat = 168
        right -= gap + searchW
        let search = NSTextField(frame: NSRect(x: right, y: controlY, width: searchW, height: controlH))
        search.cell = CenteredFieldCell(textCell: "")
        (search.cell as? CenteredFieldCell)?.textInset = 12
        search.placeholderString = "搜索快捷项"
        search.stringValue = panelSearch
        search.font = UI.font(12)
        search.textColor = color("text.primary")
        search.isBordered = false
        search.isBezeled = false
        search.drawsBackground = true
        search.backgroundColor = color("bg.input")
        search.target = self
        search.action = #selector(panelSearchChanged(_:))
        search.cell?.sendsActionOnEndEditing = false
        search.focusRingType = .none
        search.wantsLayer = true
        search.layer?.cornerRadius = 8
        search.layer?.masksToBounds = true
        search.layer?.borderWidth = 1
        search.layer?.borderColor = color("border.strong").cgColor
        search.autoresizingMask = [.minXMargin]
        header.addSubview(search)
        let edit = panelEdit
            ? primaryButton("✓ 完成", #selector(togglePanelEdit))
            : softButton("✎ 编辑", #selector(togglePanelEdit))
        right -= gap + 76
        edit.frame = NSRect(x: right, y: controlY, width: 76, height: controlH)
        edit.autoresizingMask = [.minXMargin]
        header.addSubview(edit)
        let addTop = primaryButton(session.store.text("empty.panelAction"), #selector(addShortcut))
        right -= gap + 76
        addTop.frame = NSRect(x: right, y: controlY, width: 76, height: controlH)
        addTop.autoresizingMask = [.minXMargin]
        header.addSubview(addTop)
        let ctx = panelNavContext()
        let contentMax = addTop.frame.minX - gap
        var cx = edge
        let brand = UI.plain("DeskKit", 13, .semibold, color("text.tertiary"))
        brand.frame = NSRect(x: cx, y: 0, width: 56, height: headerH)
        brand.autoresizingMask = [.maxXMargin]
        header.addSubview(brand)
        cx += 56 + 10
        if let dot = ctx.color {
            let bar = NSView(frame: NSRect(x: cx, y: (headerH - 14) / 2, width: 3, height: 14))
            bar.wantsLayer = true
            bar.layer?.cornerRadius = 2
            bar.layer?.backgroundColor = dot.cgColor
            header.addSubview(bar)
            cx += 11
        }
        let countText = " · \(ctx.count) 项"
        let countW = PlainLabel.width(for: countText, font: UI.font(12)) + 6
        let titleW = max(48, contentMax - cx - countW)
        let title = UI.plain(ctx.title, 16, .semibold, color("text.primary"))
        title.truncates = true
        title.frame = NSRect(x: cx, y: 0, width: titleW, height: headerH)
        title.autoresizingMask = [.maxXMargin]
        header.addSubview(title)
        let meta = UI.plain(countText, 12, .regular, color("text.tertiary"))
        meta.frame = NSRect(x: title.frame.maxX, y: 0, width: countW, height: headerH)
        header.addSubview(meta)

        let line = NSView(frame: NSRect(x: 0, y: headerH - 1, width: size.width, height: 1))
        line.wantsLayer = true
        line.layer?.backgroundColor = color("border.default").cgColor
        line.autoresizingMask = [.width, .minYMargin]
        header.addSubview(line)
        header.autoresizingMask = [.width, .maxYMargin]
        header.onDragEnd = { [weak self, weak window] in self?.rememberPanelFrame(window) }
        root.addSubview(header)

        let navW = session.store.layout("windows", "navWidth")
        let nav = WindowDragView(frame: NSRect(x: 0, y: headerH, width: navW, height: size.height - headerH - footerH))
        paint(nav, "bg.float")
        var ny: CGFloat = 16
        let head = UI.plain("分组", 11, .semibold, color("text.tertiary"))
        head.frame = NSRect(x: edge, y: ny, width: navW - edge * 2, height: 16)
        nav.addSubview(head)
        ny += 28
        let total = session.library.items.count
        ny = navItem(nav, ny, navW, "all", "全部", total, nil)

        let rowH: CGFloat = 36
        let rowGap: CGFloat = 4
        let groups = session.library.groups.filter { $0.id != "fav" }
        let listH = groups.isEmpty ? 0 : CGFloat(groups.count) * (rowH + rowGap) - rowGap
        let list = GroupNavListView(frame: NSRect(x: 0, y: ny, width: navW, height: listH))
        list.onReorder = { [weak self] source, target, after in
            self?.session.reorderGroup(source, relativeTo: target, placeAfter: after)
            self?.renderGroups()
        }
        var ly: CGFloat = 0
        for group in groups {
            let groupRow = NavGroupRow(frame: NSRect(x: edge, y: ly, width: navW - edge * 2, height: rowH))
            groupRow.groupId = group.id
            groupRow.selected = panelNav == group.id
            groupRow.accentSoft = color("accent.soft")
            groupRow.accent = color("accent")
            let titleColor = panelNav == group.id ? color("accent.hover") : color("text.secondary")
            groupRow.apply(
                title: group.name,
                count: group.itemIds.count,
                dotColor: Theme.paint(group.color),
                titleColor: titleColor,
                countColor: color("text.tertiary"),
                renaming: renamingGroupId == group.id,
                renameTarget: self,
                renameAction: #selector(commitGroupName(_:)),
                renameId: "rename-\(group.id)"
            )
            groupRow.onRightClick = { [weak self] event in self?.showGroupMenu(id: group.id, event: event, view: groupRow) }
            groupRow.onClick = { [weak self] in
                self?.panelNav = group.id
                self?.renderGroups()
            }
            list.addRow(groupRow)
            ly += rowH + rowGap
        }
        nav.addSubview(list)
        ny += listH

        let add = softButton("新建分组", #selector(addGroup))
        add.frame = NSRect(x: edge, y: edge, width: navW - edge * 2, height: 34)
        add.autoresizingMask = [.width, .maxYMargin]
        nav.addSubview(add)
        nav.autoresizingMask = [.height]
        nav.onDragEnd = { [weak self, weak window] in self?.rememberPanelFrame(window) }
        root.addSubview(nav)

        let scroll = NSScrollView(frame: NSRect(x: navW, y: headerH, width: size.width - navW, height: size.height - headerH - footerH))
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.scrollerStyle = .overlay
        scroll.borderType = .noBorder
        scroll.automaticallyAdjustsContentInsets = false
        scroll.contentInsets = NSEdgeInsets(top: 0, left: 0, bottom: 0, right: scrollGutter)
        let canvas = FlippedView(frame: NSRect(x: 0, y: 0, width: scroll.frame.width, height: 10))
        canvas.autoresizingMask = [.width]
        scroll.documentView = canvas
        scroll.autoresizingMask = [.width, .height]
        root.addSubview(scroll)
        drawPanelGroups(into: canvas)
        applyBackground(to: root)

        let foot = WindowDragView(frame: NSRect(x: 0, y: size.height - footerH, width: size.width, height: footerH))
        paint(foot, "bg.float")
        let chord = hotkeyText("panel")
        let footerText = session.store.text("panel.footer", ["hotkey": chord, "count": "\(total)"])
        let tip = UI.plain(footerText, 12, .regular, color("text.tertiary"))
        tip.truncates = true
        var tipW = size.width - navW - edge * 2
        if panelEdit {
            let done = primaryButton("✓ 完成", #selector(togglePanelEdit))
            done.frame = NSRect(x: size.width - edge - 96, y: (footerH - controlH) / 2, width: 96, height: controlH)
            done.autoresizingMask = [.minXMargin]
            foot.addSubview(done)
            tipW = done.frame.minX - CGFloat(navW) - edge - gap
        }
        tip.frame = NSRect(x: navW + edge, y: 0, width: max(80, tipW), height: footerH)
        tip.autoresizingMask = [.width]
        foot.addSubview(tip)
        let footLine = NSView(frame: NSRect(x: 0, y: 0, width: size.width, height: 1))
        footLine.wantsLayer = true
        footLine.layer?.backgroundColor = color("border.default").cgColor
        footLine.autoresizingMask = [.width]
        foot.addSubview(footLine)
        foot.autoresizingMask = [.width, .minYMargin]
        foot.onDragEnd = { [weak self, weak window] in self?.rememberPanelFrame(window) }
        root.addSubview(foot)

        drop.onResizeEnd = { [weak self, weak window] in
            self?.rememberPanelFrame(window)
            self?.renderGroups()
        }
        installPanelDrop(drop)
        groupsWindow = window
        if let liveFrame {
            window.setFrame(liveFrame, display: false)
        }
        present(window)
        if let id = renamingGroupId {
            DispatchQueue.main.async { [weak self] in
                guard let self, let field = self.groupsWindow?.contentView?.find(id: "rename-\(id)") as? NSTextField else { return }
                self.groupsWindow?.makeFirstResponder(field)
            }
        }
        Log.info("打开快捷启动面板 \(Int(size.width))x\(Int(size.height))")
    }

    private func panelNavContext() -> (title: String, count: Int, color: NSColor?) {
        if panelNav == "all" { return ("全部", session.library.items.count, nil) }
        if let group = session.library.groups.first(where: { $0.id == panelNav }) {
            return (group.name, group.itemIds.count, Theme.paint(group.color))
        }
        return ("全部", session.library.items.count, nil)
    }

    private func navItem(_ nav: NSView, _ y: CGFloat, _ navW: CGFloat, _ id: String, _ title: String, _ count: Int, _ dot: NSColor?) -> CGFloat {
        let on = panelNav == id
        let rowH: CGFloat = 36
        let edge = uiEdge()
        let row = HitRow(frame: NSRect(x: edge, y: y, width: navW - edge * 2, height: rowH))
        row.wantsLayer = true
        row.layer?.cornerRadius = 8
        row.layer?.backgroundColor = (on ? color("accent.soft") : NSColor.clear).cgColor
        row.onClick = { [weak self] in
            self?.panelNav = id
            self?.renderGroups()
        }
        let labelX: CGFloat = 8
        let label = UI.plain(title, 13, on ? .semibold : .regular, on ? color("accent.hover") : color("text.secondary"))
        label.truncates = true
        label.frame = NSRect(x: labelX, y: 0, width: row.bounds.width - labelX - 44, height: rowH)
        row.addSubview(label)
        let c = UI.plain("\(count)", 11, .regular, color("text.tertiary"), .right)
        c.frame = NSRect(x: row.bounds.width - 40, y: 0, width: 28, height: rowH)
        row.addSubview(c)
        nav.addSubview(row)
        return y + rowH + 4
    }

    private func showGroupMenu(id: String, event: NSEvent, view: NSView) {
        guard id != "all" else { return }
        let menu = NSMenu()
        let rename = NSMenuItem(title: session.store.text("group.rename"), action: #selector(menuRenameGroup(_:)), keyEquivalent: "")
        rename.target = self
        rename.representedObject = id
        menu.addItem(rename)
        if id != "ungrouped" {
            let del = NSMenuItem(title: session.store.text("group.delete"), action: #selector(menuDeleteGroup(_:)), keyEquivalent: "")
            del.target = self
            del.representedObject = id
            menu.addItem(del)
        }
        NSMenu.popUpContextMenu(menu, with: event, for: view)
    }

    @objc private func menuRenameGroup(_ sender: NSMenuItem) {
        renamingGroupId = sender.representedObject as? String
        renderGroups()
    }

    @objc private func menuDeleteGroup(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        confirmDeleteGroup(id: id)
    }

    private func panelDashedDropZone(on view: NSView, cornerRadius: CGFloat) {
        view.wantsLayer = true
        let shape = CAShapeLayer()
        shape.path = CGPath(roundedRect: view.bounds, cornerWidth: cornerRadius, cornerHeight: cornerRadius, transform: nil)
        shape.strokeColor = color("border.strong").cgColor
        shape.fillColor = color("bg.float").withAlphaComponent(0.4).cgColor
        shape.lineWidth = 1
        shape.lineDashPattern = [4, 3]
        view.layer?.addSublayer(shape)
    }

    private func panelPanelHintRow(width: CGFloat) -> NSView {
        let beforeText = session.store.text("empty.panelHintBefore")
        let actionText = session.store.text("empty.panelHintAction")
        let beforeW = PlainLabel.width(for: beforeText, font: UI.font(12))
        let actionW = PlainLabel.width(for: actionText, font: UI.font(12))
        let row = FlippedView(frame: NSRect(x: 0, y: 0, width: width, height: 20))
        var x = max(0, (width - beforeW - actionW) / 2)
        let before = UI.plain(beforeText, 12, .regular, color("text.tertiary"))
        before.frame = NSRect(x: x, y: 0, width: beforeW, height: 20)
        row.addSubview(before)
        x += beforeW
        let action = UI.plain(actionText, 12, .regular, color("accent.hover"))
        action.frame = NSRect(x: x, y: 0, width: actionW, height: 20)
        row.addSubview(action)
        return row
    }

    private func drawPanelFocus(into canvas: NSView) {
        guard let group = session.library.groups.first(where: { $0.id == panelNav }) else {
            canvas.frame.size.height = 40
            return
        }
        let items = group.itemIds.compactMap { id in session.library.items.first { $0.id == id } }
            .filter { panelSearch.isEmpty || $0.title.localizedCaseInsensitiveContains(panelSearch) }
        let outer = uiEdge()
        let contentW = panelContentWidth(canvas)
        let cell: CGFloat = 80
        let cellGap: CGFloat = 8
        let innerW = contentW - outer * 2

        if items.isEmpty, !panelEdit {
            let totalH: CGFloat = 420
            let zoneW = min(420, innerW)
            let zoneH: CGFloat = 104
            let blockH: CGFloat = zoneH + 88
            let top = max(outer, (totalH - blockH) / 2)
            let zone = FlippedView(frame: NSRect(x: outer + (contentW - outer * 2 - zoneW) / 2, y: top, width: zoneW, height: zoneH))
            panelDashedDropZone(on: zone, cornerRadius: 10)
            let down = UI.plain("↓", 28, .regular, color("text.tertiary"), .center)
            down.frame = NSRect(x: 0, y: 24, width: zoneW, height: 32)
            zone.addSubview(down)
            let sub = UI.plain(session.store.text("empty.panelDrop"), 12, .regular, color("text.tertiary"), .center)
            sub.frame = NSRect(x: 16, y: 58, width: zoneW - 32, height: 20)
            zone.addSubview(sub)
            canvas.addSubview(zone)
            let empty = UI.plain(session.store.text("empty.panel"), 13, .regular, color("text.secondary"), .center)
            empty.frame = NSRect(x: outer, y: zone.frame.maxY + 20, width: innerW, height: 22)
            canvas.addSubview(empty)
            let hint = panelPanelHintRow(width: innerW)
            hint.frame = NSRect(x: outer, y: empty.frame.maxY + 8, width: innerW, height: 20)
            canvas.addSubview(hint)
            canvas.frame.size.height = totalH
            return
        }

        var cx = outer
        var cy = outer
        for (index, item) in items.enumerated() {
            let cellView = HitRow(frame: NSRect(x: cx, y: cy, width: cell, height: cell))
            cellView.wantsLayer = true
            cellView.layer?.cornerRadius = 8
            if panelEdit {
                let del = HitRow(frame: NSRect(x: 2, y: 2, width: 18, height: 18))
                del.wantsLayer = true
                del.layer?.cornerRadius = 4
                del.layer?.borderWidth = 1
                del.layer?.borderColor = color("border.strong").cgColor
                del.layer?.backgroundColor = color("bg.float").cgColor
                let minus = UI.plain("−", 14, .regular, color("text.secondary"), .center)
                minus.frame = del.bounds
                del.addSubview(minus)
                let itemId = item.id
                del.onClick = { [weak self] in self?.removeShortcut(id: itemId) }
                cellView.addSubview(del)
            }
            let fileIcon = AppIcons.file(item.path, size: 40)
            let iconImage = fileIcon ?? AppIcons.symbol(item.kind == "url" ? "link" : "app.dashed", size: 40, tint: color("accent"))
            let icon = AppIcons.view(image: iconImage, frame: NSRect(x: (cell - 40) / 2, y: 6, width: 40, height: 40))
            cellView.addSubview(icon)
            let lbl = UI.plain(item.title, 11, .regular, color("text.secondary"), .center)
            lbl.truncates = true
            lbl.frame = NSRect(x: 4, y: 50, width: cell - 8, height: 20)
            cellView.addSubview(lbl)
            let shortcut = item
            if panelEdit {
                cellView.onClick = {}
            } else {
                cellView.onClick = { [weak self] in self?.openShortcut(shortcut) }
            }
            if panelEdit { wobble(cellView, index: index) }
            canvas.addSubview(cellView)
            cx += cell + cellGap
            if cx + cell > outer + contentW - outer {
                cx = outer
                cy += cell + cellGap
            }
        }
        if panelEdit {
            let add = HitRow(frame: NSRect(x: cx, y: cy, width: cell, height: cell))
            panelDashedDropZone(on: add, cornerRadius: 8)
            let plus = UI.label("＋", 18, .regular, color("text.tertiary"))
            plus.alignment = .center
            plus.frame = NSRect(x: 0, y: 22, width: 80, height: 24)
            let tip = UI.label("添加", 11, .regular, color("text.tertiary"))
            tip.alignment = .center
            tip.frame = NSRect(x: 0, y: 48, width: 80, height: 16)
            add.addSubview(plus)
            add.addSubview(tip)
            add.onClick = { [weak self] in self?.addShortcut() }
            canvas.addSubview(add)
            cy += cell + cellGap
        }
        canvas.frame.size.height = max(cy + cell + outer, 120)
    }

    private func drawPanelGroups(into canvas: NSView) {
        if panelNav != "all" {
            drawPanelFocus(into: canvas)
            return
        }
        let groups = session.library.groups.filter { $0.id != "fav" }
        let outer = uiEdge()
        let inner = uiEdge()
        let contentW = panelContentWidth(canvas)
        let cardGap: CGFloat = 12
        var y: CGFloat = outer
        for group in groups {
            let items = group.itemIds.compactMap { id in session.library.items.first { $0.id == id } }
                .filter { panelSearch.isEmpty || $0.title.localizedCaseInsensitiveContains(panelSearch) }
            let folded = foldedGroups.contains(group.id)
            let cell: CGFloat = 80
            let cellGap: CGFloat = 8
            let cardW = contentW - outer * 2
            let cols = max(1, Int((cardW - inner * 2 + cellGap) / (cell + cellGap)))
            let rows = Int(ceil(Double(max(items.count + (panelEdit ? 1 : 0), 1)) / Double(cols)))
            let headH: CGFloat = 48
            let cardH: CGFloat = folded ? headH : (items.isEmpty ? 176 : headH + 8 + CGFloat(rows) * cell + CGFloat(max(rows - 1, 0)) * cellGap + inner)
            let card = FlippedView(frame: NSRect(x: outer, y: y, width: cardW, height: cardH))
            card.identifier = NSUserInterfaceItemIdentifier("group-\(group.id)")
            card.wantsLayer = true
            card.layer?.backgroundColor = NSColor.clear.cgColor
            card.layer?.cornerRadius = 10
            card.layer?.masksToBounds = true
            card.layer?.borderWidth = 1
            card.layer?.borderColor = color("border.default").cgColor
            let effect = NSVisualEffectView(frame: card.bounds)
            effect.material = prefersDark ? .hudWindow : .popover
            effect.blendingMode = .withinWindow
            effect.state = .active
            card.addSubview(effect)
            let tint = NSView(frame: card.bounds)
            tint.wantsLayer = true
            tint.layer?.backgroundColor = color("bg.float").withAlphaComponent(0.7).cgColor
            card.addSubview(tint)

            let bar = NSView(frame: NSRect(x: inner, y: (headH - 14) / 2, width: 3, height: 14))
            bar.wantsLayer = true
            bar.layer?.backgroundColor = Theme.paint(group.color).cgColor
            bar.layer?.cornerRadius = 2
            card.addSubview(bar)
            let btn: CGFloat = 28
            let btnY = (headH - btn) / 2
            var nameLimit = card.bounds.width - inner
            let fold = symbolButton(folded ? "chevron.right" : "chevron.down", #selector(toggleFold(_:)), point: 12)
            fold.identifier = NSUserInterfaceItemIdentifier(group.id)
            fold.frame = NSRect(x: card.bounds.width - inner - btn, y: btnY, width: btn, height: btn)
            card.addSubview(fold)
            nameLimit = fold.frame.minX - 12
            let countText = "\(items.count) 项"
            let countW = PlainLabel.width(for: countText, font: UI.font(11)) + 4
            let nameX: CGFloat = inner + 3 + 10
            let nameW = min(PlainLabel.width(for: group.name, font: UI.font(13, .semibold)), max(40, nameLimit - nameX - countW - 8))
            let name = UI.plain(group.name, 13, .semibold, color("text.primary"))
            name.truncates = true
            name.frame = NSRect(x: nameX, y: 0, width: nameW, height: headH)
            card.addSubview(name)
            let count = UI.plain(countText, 11, .regular, color("text.tertiary"))
            count.frame = NSRect(x: name.frame.maxX + 8, y: 0, width: countW, height: headH)
            card.addSubview(count)

            if folded {
                canvas.addSubview(card)
                y += cardH + cardGap
                continue
            }

            if items.isEmpty, !panelEdit {
                let empty = UI.plain(session.store.text("empty.panelStack"), 13, .regular, color("text.tertiary"), .center)
                empty.frame = NSRect(x: 24, y: 64, width: card.bounds.width - 48, height: 48)
                card.addSubview(empty)
            } else {
                var cx: CGFloat = inner
                var cy: CGFloat = headH + 4
                for (index, item) in items.enumerated() {
                    let cellView = HitRow(frame: NSRect(x: cx, y: cy, width: cell, height: cell))
                    cellView.wantsLayer = true
                    cellView.layer?.cornerRadius = 8
                    if panelEdit {
                        let del = HitRow(frame: NSRect(x: 2, y: 2, width: 18, height: 18))
                        del.wantsLayer = true
                        del.layer?.cornerRadius = 4
                        del.layer?.borderWidth = 1
                        del.layer?.borderColor = color("border.strong").cgColor
                        del.layer?.backgroundColor = color("bg.float").cgColor
                        let minus = UI.plain("−", 14, .regular, color("text.secondary"), .center)
                        minus.frame = del.bounds
                        del.addSubview(minus)
                        let itemId = item.id
                        del.onClick = { [weak self] in self?.removeShortcut(id: itemId) }
                        cellView.addSubview(del)
                    }
                    let fileIcon = AppIcons.file(item.path, size: 40)
                    if fileIcon == nil {
                        Log.warn("快捷项没有图标 \(item.title) 路径 \(item.path)")
                    }
                    let iconImage = fileIcon ?? AppIcons.symbol(item.kind == "url" ? "link" : "app.dashed", size: 40, tint: color("accent"))
                    let icon = AppIcons.view(image: iconImage, frame: NSRect(x: (cell - 40) / 2, y: 6, width: 40, height: 40))
                    cellView.addSubview(icon)
                    let lbl = UI.plain(item.title, 11, .regular, color("text.secondary"), .center)
                    lbl.truncates = true
                    lbl.frame = NSRect(x: 4, y: 50, width: cell - 8, height: 20)
                    cellView.addSubview(lbl)
                    let shortcut = item
                    if panelEdit {
                        cellView.onClick = {}
                    } else {
                        cellView.onClick = { [weak self] in self?.openShortcut(shortcut) }
                    }
                    if panelEdit { wobble(cellView, index: index) }
                    card.addSubview(cellView)
                    cx += cell + cellGap
                    if cx + cell > card.bounds.width - inner {
                        cx = inner
                        cy += cell + cellGap
                    }
                }
                if panelEdit {
                    let add = HitRow(frame: NSRect(x: cx, y: cy, width: cell, height: cell))
                    panelDashedDropZone(on: add, cornerRadius: 8)
                    let plus = UI.label("＋", 18, .regular, color("text.tertiary"))
                    plus.alignment = .center
                    plus.frame = NSRect(x: 0, y: 22, width: 80, height: 24)
                    let tip = UI.label("添加", 11, .regular, color("text.tertiary"))
                    tip.alignment = .center
                    tip.frame = NSRect(x: 0, y: 48, width: 80, height: 16)
                    add.addSubview(plus)
                    add.addSubview(tip)
                    add.onClick = { [weak self] in self?.addShortcut() }
                    card.addSubview(add)
                }
            }
            canvas.addSubview(card)
            y += cardH + cardGap
        }
        canvas.frame.size.height = max(canvas.frame.height, y)
    }

    private func wobble(_ view: NSView, index: Int) {
        view.wantsLayer = true
        let anim = CABasicAnimation(keyPath: "transform.rotation.z")
        anim.fromValue = -0.04
        anim.toValue = 0.04
        anim.duration = 0.12
        anim.autoreverses = true
        anim.repeatCount = .infinity
        anim.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        anim.timeOffset = Double(index % 2) * 0.06
        anim.isRemovedOnCompletion = false
        view.layer?.add(anim, forKey: "wobble")
    }

    // MARK: - Tools C

    private func openToolGrid() {
        toolMode = "grid"
        activeTool = ""
        let w = session.store.layout("tools", "gridWidth")
        let h = session.store.layout("tools", "gridHeight")
        let window = toolWindow ?? makeWindow("常用工具", NSSize(width: w, height: h))
        window.setContentSize(NSSize(width: w, height: h))
        let root = FlippedView(frame: NSRect(x: 0, y: 0, width: w, height: h))
        window.contentView = root
        paint(root, "bg.float")
        card(root)
        root.wantsLayer = true
        root.layer?.cornerRadius = 14

        let head = UI.label("常用工具", 16, .semibold, color("text.primary"))
        head.frame = NSRect(x: 16, y: 12, width: 160, height: 24)
        root.addSubview(head)
        let hint = UI.label(session.clips.isEmpty ? "剪贴板：无新内容" : "剪贴板：1 条新", 12, .regular, color("text.tertiary"))
        hint.frame = NSRect(x: 180, y: 16, width: 200, height: 16)
        root.addSubview(hint)

        let order = ["clipboard", "picker", "translate", "calc", "timestamp", "unit", "color", "qr", "add"]
        let footH: CGFloat = 32
        let topH: CGFloat = 44
        let gap: CGFloat = 8
        let rows: CGFloat = 3
        let cols: CGFloat = 3
        let availH = h - topH - footH - 8
        let availW = w - 24
        let cellH = min(session.store.layout("tools", "cellHeight"), floor((availH - gap * (rows - 1)) / rows))
        let cellW = min(session.store.layout("tools", "cellWidth"), floor((availW - gap * (cols - 1)) / cols))
        let gridW = cols * cellW + gap * (cols - 1)
        let originX = (w - gridW) / 2
        for (index, id) in order.enumerated() {
            let col = CGFloat(index % 3)
            let row = CGFloat(index / 3)
            let cell = HitRow(frame: NSRect(x: originX + col * (cellW + gap), y: topH + row * (cellH + gap), width: cellW, height: cellH))
            cell.wantsLayer = true
            cell.layer?.cornerRadius = 10
            cell.layer?.borderWidth = 1
            cell.layer?.borderColor = color(id == "add" ? "border.strong" : "border.default").cgColor
            if id == "add" {
                cell.layer?.backgroundColor = color("bg.subtle").cgColor
            }
            let symbol = id == "add" ? "plus" : AppIcons.symbolName(toolId: id, kind: "tool")
            let tint = id == "add" ? color("text.tertiary") : color("accent")
            let icon = AppIcons.view(image: AppIcons.symbol(symbol, size: 28, tint: tint), frame: NSRect(x: (cellW - 28) / 2, y: 12, width: 28, height: 28))
            cell.addSubview(icon)
            let lbl = UI.label(id == "add" ? "排序 / 显示" : toolTitle(id), 12, .regular, color("text.secondary"))
            lbl.alignment = .center
            lbl.frame = NSRect(x: 4, y: 46, width: cellW - 8, height: 16)
            cell.addSubview(lbl)
            let num = UI.label("\(index + 1)", 11, .semibold, color("text.tertiary"))
            num.alignment = .right
            num.frame = NSRect(x: cellW - 22, y: cellH - 18, width: 16, height: 14)
            cell.addSubview(num)
            let toolId = id
            cell.onClick = { [weak self] in
                guard let self else { return }
                if toolId == "add" {
                    self.session.settings.hiddenTools = self.session.settings.hiddenTools.isEmpty ? ["qr"] : []
                    self.session.save()
                    Log.info("切换工具显示，隐藏 \(self.session.settings.hiddenTools.joined(separator: ","))")
                    self.openToolGrid()
                } else {
                    self.openToolPage(toolId)
                }
            }
            root.addSubview(cell)
        }

        let foot = FlippedView(frame: NSRect(x: 0, y: h - footH, width: w, height: footH))
        paint(foot, "bg.subtle")
        let footText = UI.label("数字键 1–9 直达 · \(session.store.formatChord(platform: "macos", action: "tools")) 打开面板 · Esc 返回", 11, .regular, color("text.tertiary"))
        footText.alignment = .center
        footText.frame = NSRect(x: 8, y: 8, width: w - 16, height: 16)
        foot.addSubview(footText)
        root.addSubview(foot)
        let close = symbolButton("xmark", #selector(closeTool))
        close.frame = NSRect(x: w - 36, y: 8, width: 28, height: 28)
        root.addSubview(close)
        applyBackground(to: root)
        toolWindow = window
        hideLauncher()
        present(window)
        Log.info("打开工具宫格 \(Int(w))x\(Int(h))")
    }

    private func openToolPage(_ id: String) {
        toolMode = "page"
        activeTool = id
        enter("TOOLPAGE")
        let w = session.store.layout("tools", "pageWidth")
        let h = session.store.layout("tools", "pageHeight")
        let window = toolWindow ?? makeWindow(toolTitle(id), NSSize(width: w, height: h))
        window.setContentSize(NSSize(width: w, height: h))
        let root = FlippedView(frame: NSRect(x: 0, y: 0, width: w, height: h))
        window.contentView = root
        paint(root, "bg.float")
        card(root)

        let top = FlippedView(frame: NSRect(x: 0, y: 0, width: w, height: 48))
        paint(top, "bg.float")
        let back = symbolButton("chevron.left", #selector(closeTool), point: 16)
        back.frame = NSRect(x: 12, y: 10, width: 36, height: 28)
        top.addSubview(back)
        let title = UI.label(toolTitle(id), 16, .semibold, color("text.primary"))
        title.frame = NSRect(x: 56, y: 12, width: 200, height: 24)
        top.addSubview(title)
        let tag = pillTag("内置工具 · 离线可用")
        tag.frame = NSRect(x: 270, y: 12, width: 140, height: 24)
        top.addSubview(tag)
        let line = NSView(frame: NSRect(x: 0, y: 47, width: w, height: 1))
        line.wantsLayer = true
        line.layer?.backgroundColor = color("border.default").cgColor
        top.addSubview(line)
        root.addSubview(top)

        fillToolPage(root, id)

        let foot = FlippedView(frame: NSRect(x: 0, y: h - 44, width: w, height: 44))
        paint(foot, "bg.subtle")
        let copy = primaryButton("Enter 复制结果", #selector(copyToolResult))
        copy.frame = NSRect(x: 16, y: 8, width: 130, height: 28)
        foot.addSubview(copy)
        let hint = UI.label("Esc 返回宫格 · 本地计算，零网络请求", 12, .regular, color("text.tertiary"))
        hint.frame = NSRect(x: 160, y: 12, width: 400, height: 18)
        foot.addSubview(hint)
        root.addSubview(foot)

        applyBackground(to: root)
        toolWindow = window
        hideLauncher()
        present(window)
        Log.info("打开工具内页 \(id) \(Int(w))x\(Int(h))")
    }

    private func fillToolPage(_ root: NSView, _ id: String) {
        switch id {
        case "calc", "unit":
            let left = pane(NSRect(x: 16, y: 64, width: 340, height: 380), "输入表达式 / 单位换算")
            let sample = id == "unit" ? "3km to mi" : "520+999-1314"
            let input = NSTextField(frame: NSRect(x: 16, y: 48, width: 308, height: 32))
            input.stringValue = sample
            input.font = UI.font(15, .medium)
            input.placeholderString = id == "unit" ? "3km to mi" : "(1+2)*3"
            input.target = self
            input.action = #selector(calcChanged(_:))
            left.addSubview(input)
            var x: CGFloat = 16
            for s in ["123*4", "520+999-1314", "(1+2)*3", "3km to mi"] {
                let chip = softButton(s, #selector(useSample(_:)))
                chip.frame = NSRect(x: x, y: 96, width: 70 + CGFloat(s.count) * 2, height: 26)
                left.addSubview(chip)
                x += chip.frame.width + 6
                if x > 280 { break }
            }
            let note = UI.label("支持四则运算与长度 / 重量 / 温度 / 离线汇率换算。", 11, .regular, color("text.tertiary"))
            note.frame = NSRect(x: 16, y: 140, width: 308, height: 36)
            note.maximumNumberOfLines = 2
            left.addSubview(note)
            root.addSubview(left)

            let right = pane(NSRect(x: 372, y: 64, width: 332, height: 380), "结果（等宽字体）")
            let solved = id == "unit" ? (UnitConverter.convert(sample) ?? "—") : ExpressionEvaluator.evaluate(sample).data
            lastToolOutput = solved
            let out = UI.label(solved, 36, .semibold, color("text.primary"))
            out.alignment = .right
            out.frame = NSRect(x: 16, y: 60, width: 300, height: 48)
            out.identifier = NSUserInterfaceItemIdentifier("tool-output")
            right.addSubview(out)
            let tip = UI.label("在左侧输入算式，结果实时出现", 12, .regular, color("text.tertiary"))
            tip.frame = NSRect(x: 16, y: 120, width: 300, height: 18)
            tip.identifier = NSUserInterfaceItemIdentifier("tool-note")
            right.addSubview(tip)
            root.addSubview(right)

        case "translate":
            let left = pane(NSRect(x: 16, y: 64, width: 340, height: 380), "输入（中英互译）")
            let input = NSTextField(frame: NSRect(x: 16, y: 48, width: 308, height: 120))
            input.stringValue = "hello world"
            input.placeholderString = "hello world / 你好"
            input.font = UI.font(14)
            input.target = self
            input.action = #selector(translateChanged(_:))
            left.addSubview(input)
            let tip = UI.label("触发词 fy 与此内页联动 · 内置离线小词典", 11, .regular, color("text.tertiary"))
            tip.frame = NSRect(x: 16, y: 180, width: 308, height: 32)
            tip.maximumNumberOfLines = 2
            left.addSubview(tip)
            root.addSubview(left)
            let right = pane(NSRect(x: 372, y: 64, width: 332, height: 380), "译文")
            let result = Translator.translate("hello world")
            lastToolOutput = result.text
            let out = UI.label(result.text, 22, .semibold, color("accent.hover"))
            out.frame = NSRect(x: 16, y: 48, width: 300, height: 80)
            out.maximumNumberOfLines = 3
            out.identifier = NSUserInterfaceItemIdentifier("tool-output")
            right.addSubview(out)
            let note = UI.label(session.store.text("translate.offline"), 12, .regular, color("text.tertiary"))
            note.frame = NSRect(x: 16, y: 140, width: 300, height: 18)
            right.addSubview(note)
            root.addSubview(right)

        case "color":
            let box = pane(NSRect(x: 16, y: 64, width: 688, height: 380), "HEX ↔ RGB ↔ HSL")
            let input = NSTextField(frame: NSRect(x: 16, y: 48, width: 200, height: 32))
            input.stringValue = "#3B6CF6"
            input.font = UI.font(15, .medium)
            input.target = self
            input.action = #selector(convertChanged(_:))
            input.identifier = NSUserInterfaceItemIdentifier("color")
            box.addSubview(input)
            let swatch = NSView(frame: NSRect(x: 16, y: 100, width: 656, height: 80))
            swatch.wantsLayer = true
            swatch.layer?.cornerRadius = 8
            swatch.layer?.backgroundColor = Theme.paint("#3B6CF6").cgColor
            swatch.identifier = NSUserInterfaceItemIdentifier("color-swatch")
            box.addSubview(swatch)
            let desc = ColorConvert.describe("#3B6CF6") ?? ""
            lastToolOutput = "#3B6CF6"
            let out = UI.label(desc, 14, .medium, color("text.primary"))
            out.frame = NSRect(x: 16, y: 200, width: 656, height: 60)
            out.maximumNumberOfLines = 3
            out.identifier = NSUserInterfaceItemIdentifier("tool-output")
            box.addSubview(out)
            root.addSubview(box)

        case "timestamp":
            let box = pane(NSRect(x: 16, y: 64, width: 688, height: 380), "时间戳 ↔ 日期")
            let input = NSTextField(frame: NSRect(x: 16, y: 48, width: 280, height: 32))
            input.stringValue = "1730000000"
            input.font = UI.font(15, .medium)
            input.target = self
            input.action = #selector(convertChanged(_:))
            input.identifier = NSUserInterfaceItemIdentifier("timestamp")
            box.addSubview(input)
            let now = softButton("取当前时间", #selector(tsNow))
            now.frame = NSRect(x: 310, y: 48, width: 110, height: 32)
            box.addSubview(now)
            let text = TimestampConvert.describe("1730000000") ?? ""
            lastToolOutput = text
            let out = UI.label(text, 18, .semibold, color("text.primary"))
            out.frame = NSRect(x: 16, y: 100, width: 640, height: 80)
            out.maximumNumberOfLines = 3
            out.identifier = NSUserInterfaceItemIdentifier("tool-output")
            box.addSubview(out)
            root.addSubview(box)

        case "clipboard":
            let box = pane(NSRect(x: 16, y: 64, width: 688, height: 380), "剪贴板历史")
            var y: CGFloat = 48
            if session.clips.isEmpty {
                let empty = UI.label(session.store.text("empty.clipboard"), 13, .regular, color("text.secondary"))
                empty.frame = NSRect(x: 16, y: y, width: 640, height: 24)
                box.addSubview(empty)
            }
            for clip in session.clips.prefix(8) {
                let row = softButton(String(clip.text.prefix(60)), #selector(copyClip(_:)))
                row.identifier = NSUserInterfaceItemIdentifier(clip.text)
                row.frame = NSRect(x: 16, y: y, width: 656, height: 30)
                box.addSubview(row)
                y += 36
            }
            let clear = softButton("清空", #selector(clearClips))
            clear.frame = NSRect(x: 16, y: 330, width: 80, height: 28)
            box.addSubview(clear)
            root.addSubview(box)

        case "picker":
            let box = pane(NSRect(x: 16, y: 64, width: 688, height: 380), "截图取色")
            let note = UI.label("需要「屏幕录制」权限。未授予时给出明确引导，不静默失败。", 13, .regular, color("text.secondary"))
            note.frame = NSRect(x: 16, y: 48, width: 640, height: 40)
            note.maximumNumberOfLines = 2
            box.addSubview(note)
            let grant = softButton("去授权", #selector(askScreen))
            grant.frame = NSRect(x: 16, y: 100, width: 100, height: 30)
            box.addSubview(grant)
            let pick = primaryButton("开始取色", #selector(pickColor))
            pick.frame = NSRect(x: 130, y: 100, width: 120, height: 30)
            box.addSubview(pick)
            let out = UI.label(lastToolOutput.isEmpty ? "—" : lastToolOutput, 22, .semibold, color("accent.hover"))
            out.frame = NSRect(x: 16, y: 160, width: 400, height: 36)
            out.identifier = NSUserInterfaceItemIdentifier("tool-output")
            box.addSubview(out)
            root.addSubview(box)

        default:
            let box = pane(NSRect(x: 16, y: 64, width: 688, height: 380), "二维码")
            let note = UI.label("二维码生成放在后续版本（P1 占位）。现在可以把文本留在剪贴板。", 13, .regular, color("text.secondary"))
            note.frame = NSRect(x: 16, y: 48, width: 640, height: 40)
            note.maximumNumberOfLines = 2
            box.addSubview(note)
            root.addSubview(box)
        }
    }

    // MARK: - Settings E

    private func renderSettings() {
        let size = NSSize(width: session.store.layout("windows", "settings", "width"), height: session.store.layout("windows", "settings", "height"))
        let window = settingsWindow ?? makeWindow("设置", size)
        let root = FlippedView(frame: NSRect(origin: .zero, size: size))
        window.contentView = root
        paint(root, "bg.canvas")
        card(root)

        let header = FlippedView(frame: NSRect(x: 0, y: 0, width: size.width, height: 48))
        paint(header, "bg.float")
        let title = UI.label("设置", 16, .semibold, color("text.primary"))
        title.frame = NSRect(x: 16, y: 12, width: 120, height: 24)
        header.addSubview(title)
        let close = symbolButton("xmark", #selector(closeSettings))
        close.frame = NSRect(x: size.width - 44, y: 10, width: 32, height: 28)
        header.addSubview(close)
        root.addSubview(header)

        let navW = session.store.layout("windows", "navWidth")
        let nav = FlippedView(frame: NSRect(x: 0, y: 48, width: navW, height: size.height - 48 - 36))
        paint(nav, "bg.float")
        let navHead = UI.label("设置", 11, .semibold, color("text.tertiary"))
        navHead.frame = NSRect(x: 16, y: 14, width: 160, height: 16)
        nav.addSubview(navHead)
        let tabs = [("general", "常规"), ("hotkeys", "快捷键"), ("about", "关于与双端信息")]
        for (index, tab) in tabs.enumerated() {
            let on = settingsTab == tab.0
            let row = HitRow(frame: NSRect(x: 8, y: 40 + CGFloat(index) * 40, width: navW - 16, height: 34))
            row.wantsLayer = true
            row.layer?.cornerRadius = 8
            row.layer?.backgroundColor = (on ? color("accent.soft") : NSColor.clear).cgColor
            let label = UI.label(tab.1, 13, on ? .semibold : .regular, on ? color("accent.hover") : color("text.secondary"))
            label.frame = NSRect(x: 12, y: 8, width: navW - 36, height: 18)
            row.addSubview(label)
            let id = tab.0
            row.onClick = { [weak self] in
                self?.settingsTab = id
                self?.renderSettings()
            }
            nav.addSubview(row)
        }
        let navLine = NSView(frame: NSRect(x: navW - 1, y: 0, width: 1, height: nav.bounds.height))
        navLine.wantsLayer = true
        navLine.layer?.backgroundColor = color("border.default").cgColor
        nav.addSubview(navLine)
        root.addSubview(nav)

        let scroll = NSScrollView(frame: NSRect(x: navW, y: 48, width: size.width - navW, height: size.height - 48 - 36))
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.borderType = .noBorder
        let doc = FlippedView(frame: NSRect(x: 0, y: 0, width: scroll.frame.width - 16, height: 10))
        let contentH = layoutSettingsPage(doc, width: doc.frame.width)
        doc.frame.size.height = contentH
        scroll.documentView = doc
        root.addSubview(scroll)

        let footBar = FlippedView(frame: NSRect(x: 0, y: size.height - 36, width: size.width, height: 36))
        paint(footBar, "bg.float")
        let footLine = NSView(frame: NSRect(x: 0, y: 0, width: size.width, height: 1))
        footLine.wantsLayer = true
        footLine.layer?.backgroundColor = color("border.default").cgColor
        footBar.addSubview(footLine)
        let foot = UI.label("所有设置自动保存 · 快捷键与设置结构在 macOS 与 Windows 使用同一套契约", 12, .regular, color("text.tertiary"))
        foot.frame = NSRect(x: 16, y: 10, width: size.width - 32, height: 16)
        footBar.addSubview(foot)
        root.addSubview(footBar)
        applyBackground(to: root)
        settingsWindow = window
        present(window)
        Log.info("打开设置 \(settingsTab)")
    }

    private func layoutSettingsPage(_ doc: FlippedView, width: CGFloat) -> CGFloat {
        let cardW = width - 32
        var y: CGFloat = 16
        if settingsTab == "general" {
            y = settingsCard(doc, y, cardW, "启动与呼出", [
                ("开机自启", "登录后自动常驻，随时响应全局呼出键", makeSwitch(session.settings.autoLaunch, #selector(toggleLaunch(_:)))),
                ("呼出方式", "全局快捷键、菜单栏，或仅从菜单打开", menuButton(summonModeTitle(), #selector(pickSummonMode(_:)))),
                ("失焦自动隐藏", "点击浮层外 150ms 后隐藏", makeSwitch(session.settings.blurHide, #selector(toggleBlur(_:)))),
                ("多屏行为", "浮层靠近屏幕边缘自动内收，不跨屏裁切", menuButton(multiScreenTitle(), #selector(pickMultiScreen(_:))))
            ])
            y = settingsCard(doc, y, cardW, "外观", [
                ("主题", "浅色为默认。深色和跟随系统会立刻切换外观", choiceChips([("light", "浅色"), ("dark", "深色"), ("system", "跟随系统")], session.settings.theme, #selector(pickTheme(_:)))),
                ("强调色", "选中结果和主按钮使用这个颜色", choiceChips([("accent", "蓝"), ("success", "绿"), ("warning", "黄"), ("danger", "红")], session.settings.accentToken, #selector(pickAccent(_:)))),
                ("结果密度", "紧凑 40px，标准 48px", choiceChips([("compact", "紧凑"), ("standard", "标准")], session.settings.density, #selector(pickDensity(_:)))),
                ("背景图片", "用于浮层、快捷启动、设置和工具页的底图", backgroundButtons()),
                ("背景透明度", "只影响底图或底色，文字和按钮保持清晰", opacityControl())
            ])
            y = settingsCard(doc, y, cardW, "数据与隐私", [
                ("本地优先", "分组、设置和剪贴板只存在本机", menuButton("管理数据", #selector(manageData(_:)))),
                ("可选云同步", "当前版本只记录开关，数据不会上传", makeSwitch(session.settings.cloudSync, #selector(toggleCloud(_:)))),
                ("剪贴板保留时长", "超过期限的记录会在下次复制时清掉", menuButton(retentionTitle(), #selector(pickRetention(_:)))),
                ("剪贴板记录", "暂停后不再写入，也可以马上清空", clipButtons()),
                ("语言", "界面文案为简体中文", valueChip("简体中文")),
                ("主程序更新", "关于页的检查更新会读取这个开关", makeSwitch(session.settings.autoUpdate, #selector(toggleUpdate(_:))))
            ])
        } else if settingsTab == "hotkeys" {
            let specs = [
                ("summon", "主呼出键", "全局呼出或隐藏搜索浮层"),
                ("panel", "快捷启动", "全局唤出或隐藏快捷启动面板"),
                ("tools", "常用工具面板", "打开 3×3 工具宫格"),
                ("clipboard", "剪贴板历史", "直达剪贴板记录"),
                ("settings", "设置页", "打开设置")
            ]
            y = settingsCard(doc, y, cardW, "快捷键 · 录制后自动保存", specs.map { item in
                (item.1, item.2, hotkeyControls(item.0))
            })
            y = triggerCard(doc, y, cardW)
            let note = settingsBox(NSRect(x: 16, y: y, width: cardW, height: 96), "被系统或其他应用占用")
            let text = UI.label("录制时需要带上 ⌘、⌥、⌃ 或 ⇧。如果和已有快捷键相同，这一行会标成冲突，不会悄悄覆盖。Esc 取消录制。", 12, .regular, color("text.secondary"))
            text.frame = NSRect(x: 16, y: 36, width: cardW - 32, height: 48)
            text.maximumNumberOfLines = 3
            note.addSubview(text)
            doc.addSubview(note)
            y += 112
        } else {
            let hero = settingsBox(NSRect(x: 16, y: y, width: cardW, height: 84), "")
            if let url = Bundle.main.url(forResource: "AppIcon", withExtension: "icns"),
               let image = NSImage(contentsOf: url) {
                hero.addSubview(AppIcons.view(image: image, frame: NSRect(x: 16, y: 18, width: 48, height: 48)))
            }
            let name = UI.label("DeskKit 0.9.0", 16, .semibold, color("text.primary"))
            name.frame = NSRect(x: 76, y: 18, width: 320, height: 22)
            hero.addSubview(name)
            let sub = UI.label("macOS 先行。Windows 使用同一套契约、同一份设置结构。", 12, .regular, color("text.secondary"))
            sub.frame = NSRect(x: 76, y: 44, width: cardW - 100, height: 18)
            hero.addSubview(sub)
            doc.addSubview(hero)
            y += 100
            let align = settingsBox(NSRect(x: 16, y: y, width: cardW, height: 52), "")
            let alignText = UI.label("双端功能对齐：核心契约已对齐。Windows 界面仍在后续按同一结构补齐。", 13, .medium, color("text.primary"))
            alignText.frame = NSRect(x: 16, y: 16, width: cardW - 32, height: 20)
            align.addSubview(alignText)
            doc.addSubview(align)
            y += 68
            y = settingsCard(doc, y, cardW, "更新日志", [
                ("0.9.0", "全局呼出、拼音搜索、内置工具、分组快捷启动、三页设置", valueChip("当前版本"))
            ])
            y = settingsCard(doc, y, cardW, "macOS 系统权限", permissionRows())
            y = settingsCard(doc, y, cardW, "数据 / 检查", [
                ("数据目录", "打开、导出或清空都作用在本机数据上", menuButton("管理数据", #selector(manageData(_:)))),
                ("检查更新", session.settings.autoUpdate ? "已开启启动检查。现在可以手动查一次" : "自动检查已关闭，仍可手动查看版本", menuButton("检查更新", #selector(checkUpdate(_:)))),
                ("重新走引导", "三步引导可以再看一遍", menuButton("重新引导", #selector(replayGuide(_:)))),
                ("实测内存", "记录当前占用，不作为发布门槛", valueChip(memoryText()))
            ])
        }
        return y + 16
    }

    private func permissionRows() -> [(String, String, NSView)] {
        let rows: [(String, String, Bool, Selector)] = [
            ("辅助功能", "全局呼出键需要这项权限", AXIsProcessTrusted(), #selector(askAccess)),
            ("屏幕录制", "截图取色需要这项权限", CGPreflightScreenCaptureAccess(), #selector(askScreen)),
            ("输入监控", "事件拦截需要这项权限", CGPreflightListenEventAccess(), #selector(askListen))
        ]
        return rows.map { item in
            let box = FlippedView(frame: NSRect(x: 0, y: 0, width: item.2 ? 72 : 156, height: 28))
            let tag = statusTag(item.2)
            tag.frame = NSRect(x: 0, y: 4, width: 72, height: 20)
            box.addSubview(tag)
            if !item.2 {
                let button = softButton("去授权", item.3)
                button.frame = NSRect(x: 76, y: 0, width: 76, height: 28)
                box.addSubview(button)
            }
            return (item.0, item.1, box)
        }
    }

    private func triggerCard(_ doc: NSView, _ y: CGFloat, _ width: CGFloat) -> CGFloat {
        let triggers = session.store.triggers.filter { $0.toolId != "ocr" && !$0.word.isEmpty }
        let height = 40 + CGFloat(triggers.count) * 56
        let card = settingsBox(NSRect(x: 16, y: y, width: width, height: height), "触发词")
        var rowY: CGFloat = 40
        for (index, item) in triggers.enumerated() {
            let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 120, height: 26))
            field.stringValue = session.settings.triggerWords[item.toolId] ?? item.word
            field.font = UI.font(13, .medium)
            field.identifier = NSUserInterfaceItemIdentifier(item.toolId)
            field.target = self
            field.action = #selector(saveTrigger(_:))
            field.placeholderString = item.word
            rowY = protoSettingRow(card, rowY, width, item.label, "输入「\(field.stringValue) + 空格」进入\(item.label)。回车保存", field, last: index == triggers.count - 1)
        }
        doc.addSubview(card)
        return y + height + 16
    }

    private func hotkeyControls(_ id: String) -> NSView {
        let box = FlippedView(frame: NSRect(x: 0, y: 0, width: 430, height: 32))
        let cap = keycap(hotkeyText(id))
        cap.frame.origin = NSPoint(x: 0, y: 5)
        box.addSubview(cap)
        let record = softButton(recordingHotkey == id ? "请按键…" : "录制", #selector(beginRecord(_:)))
        record.identifier = NSUserInterfaceItemIdentifier(id)
        record.frame = NSRect(x: cap.frame.width + 8, y: 2, width: 84, height: 28)
        box.addSubview(record)
        let reset = softButton("恢复默认", #selector(resetHotkey(_:)))
        reset.identifier = NSUserInterfaceItemIdentifier(id)
        reset.frame = NSRect(x: record.frame.maxX + 8, y: 2, width: 84, height: 28)
        box.addSubview(reset)
        let conflict = hotkeyConflict(id)
        let status = UI.label(conflict ?? "可用", 11, .semibold, conflict == nil ? color("success") : color("danger"))
        status.frame = NSRect(x: reset.frame.maxX + 8, y: 8, width: 88, height: 16)
        box.addSubview(status)
        box.frame.size.width = status.frame.maxX
        return box
    }

    private func choiceChips(_ options: [(String, String)], _ current: String, _ action: Selector) -> NSView {
        let box = FlippedView(frame: NSRect(x: 0, y: 0, width: 10, height: 28))
        var x: CGFloat = 0
        for option in options {
            let button = softButton(option.1, action)
            button.identifier = NSUserInterfaceItemIdentifier(option.0)
            let w = max(52, CGFloat(option.1.count) * 14 + 18)
            button.frame = NSRect(x: x, y: 0, width: w, height: 28)
            if option.0 == current {
                button.layer?.backgroundColor = color("accent.soft").cgColor
                button.layer?.borderColor = color("accent").cgColor
            }
            box.addSubview(button)
            x += w + 6
        }
        box.frame.size.width = max(10, x)
        return box
    }

    private func menuButton(_ title: String, _ action: Selector) -> NSButton {
        let button = softButton(title, action)
        button.frame = NSRect(x: 0, y: 0, width: max(96, CGFloat(title.count) * 14 + 24), height: 28)
        return button
    }

    private func clipButtons() -> NSView {
        let box = FlippedView(frame: NSRect(x: 0, y: 0, width: 168, height: 28))
        let pause = softButton(session.settings.clipboardPaused ? "恢复记录" : "暂停记录", #selector(togglePauseFromButton))
        pause.frame = NSRect(x: 0, y: 0, width: 84, height: 28)
        let clear = softButton("清空", #selector(clearClipboardHistory))
        clear.frame = NSRect(x: 88, y: 0, width: 72, height: 28)
        box.addSubview(pause)
        box.addSubview(clear)
        return box
    }

    private func settingsCard(_ doc: NSView, _ y: CGFloat, _ width: CGFloat, _ title: String, _ rows: [(String, String, NSView)]) -> CGFloat {
        let height = 40 + CGFloat(rows.count) * 56
        let card = settingsBox(NSRect(x: 16, y: y, width: width, height: height), title)
        var rowY: CGFloat = 40
        for (index, row) in rows.enumerated() {
            rowY = protoSettingRow(card, rowY, width, row.0, row.1, row.2, last: index == rows.count - 1)
        }
        doc.addSubview(card)
        return y + height + 16
    }

    private func protoSettingRow(_ parent: NSView, _ y: CGFloat, _ width: CGFloat, _ title: String, _ detail: String, _ control: NSView, last: Bool) -> CGFloat {
        let titleLabel = UI.label(title, 13, .medium, color("text.primary"))
        titleLabel.frame = NSRect(x: 16, y: y + 8, width: width - 220, height: 18)
        let detailLabel = UI.label(detail, 11, .regular, color("text.tertiary"))
        detailLabel.frame = NSRect(x: 16, y: y + 28, width: width - 220, height: 16)
        parent.addSubview(titleLabel)
        parent.addSubview(detailLabel)
        control.frame.origin = NSPoint(x: width - control.frame.width - 16, y: y + 16)
        parent.addSubview(control)
        if !last {
            let line = NSView(frame: NSRect(x: 16, y: y + 55, width: width - 32, height: 1))
            line.wantsLayer = true
            line.layer?.backgroundColor = color("border.default").cgColor
            parent.addSubview(line)
        }
        return y + 56
    }

    private func keycap(_ text: String) -> NSView {
        let width = max(72, KeycapLabel.width(for: text))
        let cap = KeycapLabel(frame: NSRect(x: 0, y: 0, width: width, height: 22))
        cap.text = text
        cap.tint = color("text.secondary")
        cap.fill = color("bg.input")
        cap.border = color("border.default")
        return cap
    }

    private func statusTag(_ granted: Bool) -> NSView {
        let label = UI.label(granted ? "已授予" : "未授予", 12, .semibold, granted ? color("success") : color("warning"))
        label.alignment = .right
        label.frame = NSRect(x: 0, y: 0, width: 72, height: 20)
        return label
    }

    // MARK: - Guide F

    private func renderGuide() {
        let size = NSSize(width: session.store.layout("windows", "guide", "width"), height: session.store.layout("windows", "guide", "height"))
        let window = guideWindow ?? makeWindow("欢迎使用 DeskKit", size)
        let root = FlippedView(frame: NSRect(origin: .zero, size: size))
        window.contentView = root
        paint(root, "bg.canvas")
        card(root)

        let header = FlippedView(frame: NSRect(x: 0, y: 0, width: size.width, height: 48))
        paint(header, "bg.float")
        let brand = UI.label("DeskKit", 16, .semibold, color("text.primary"))
        brand.frame = NSRect(x: 24, y: 12, width: 120, height: 24)
        header.addSubview(brand)
        for i in 1...3 {
            let dot = NSView(frame: NSRect(x: size.width / 2 - 28 + CGFloat(i - 1) * 20, y: 20, width: 10, height: 10))
            dot.wantsLayer = true
            dot.layer?.cornerRadius = 5
            dot.layer?.backgroundColor = (i == guideStep ? color("accent") : color("border.strong")).cgColor
            header.addSubview(dot)
        }
        root.addSubview(header)

        let titles = ["一个键，办完一件事", "设置呼出键", "权限与预置工具"]
        let bodies = [
            "按一下呼出，找应用、找文件、翻译、计算、取色都在同一个框里。没有插件市场，内置工具开箱即用。",
            "默认呼出键是 \(session.store.formatChord(platform: "macos", action: "summon"))。在系统设置里允许 DeskKit 后，这个键会盖过其他启动器。",
            "macOS 需要辅助功能、输入监控、屏幕录制。未授予时给出明确引导，不会返回空数据假装成功。预置 5 组工具包默认全选。"
        ]
        let title = UI.label(titles[guideStep - 1], 22, .semibold, color("text.primary"))
        title.frame = NSRect(x: 48, y: 80, width: 660, height: 32)
        root.addSubview(title)
        let body = UI.wrap(bodies[guideStep - 1], 14, .regular, color("text.secondary"), width: 660)
        body.frame = NSRect(x: 48, y: 128, width: 660, height: 80)
        root.addSubview(body)

        if guideStep == 1 {
            let card = settingsBox(NSRect(x: 48, y: 230, width: 664, height: 140), "你会用到")
            let lines = ["主启动器：搜索应用 / 文件 / 网址 / 内置工具", "快捷启动面板：分组图标网格", "常用工具：剪贴板、翻译、计算器、取色…"]
            for (i, line) in lines.enumerated() {
                let label = UI.label(line, 13, .regular, color("text.secondary"))
                label.frame = NSRect(x: 16, y: 48 + CGFloat(i) * 28, width: 620, height: 20)
                card.addSubview(label)
            }
            root.addSubview(card)
        } else if guideStep == 2 {
            let key = UI.label(session.store.formatChord(platform: "macos", action: "summon"), 32, .semibold, color("accent"))
            key.frame = NSRect(x: 48, y: 230, width: 400, height: 44)
            root.addSubview(key)
            let grant = primaryButton("打开辅助功能设置", #selector(askAccess))
            grant.frame = NSRect(x: 48, y: 300, width: 180, height: 34)
            root.addSubview(grant)
            let status = UI.label(AXIsProcessTrusted() ? "辅助功能：已授予" : "辅助功能：未授予（请允许 /Applications/DeskKit.app）", 13, .regular, color("text.secondary"))
            status.frame = NSRect(x: 48, y: 350, width: 600, height: 20)
            root.addSubview(status)
        } else {
            let perms = [
                ("辅助功能", AXIsProcessTrusted()),
                ("输入监控", CGPreflightListenEventAccess()),
                ("屏幕录制", CGPreflightScreenCaptureAccess())
            ]
            var y: CGFloat = 230
            for item in perms {
                let row = settingsBox(NSRect(x: 48, y: y, width: 664, height: 52), "")
                row.subviews.forEach { $0.removeFromSuperview() }
                let name = UI.label(item.0, 14, .semibold, color("text.primary"))
                name.frame = NSRect(x: 16, y: 16, width: 200, height: 20)
                row.addSubview(name)
                let chip = valueChip(item.1 ? "已授予" : "未授予")
                chip.frame.origin = NSPoint(x: 520, y: 16)
                row.addSubview(chip)
                root.addSubview(row)
                y += 64
            }
        }

        let foot = FlippedView(frame: NSRect(x: 0, y: size.height - 56, width: size.width, height: 56))
        paint(foot, "bg.float")
        let skip = softButton("跳过", #selector(guideSkip))
        skip.frame = NSRect(x: 24, y: 12, width: 80, height: 32)
        foot.addSubview(skip)
        if guideStep > 1 {
            let prev = softButton("上一步", #selector(guidePrev))
            prev.frame = NSRect(x: size.width - 280, y: 12, width: 90, height: 32)
            foot.addSubview(prev)
        }
        let next = primaryButton(guideStep == 3 ? "开始使用" : "下一步", #selector(guideNext))
        next.frame = NSRect(x: size.width - 170, y: 12, width: 140, height: 32)
        foot.addSubview(next)
        root.addSubview(foot)

        guideWindow = window
        present(window)
        Log.info("引导 Step \(guideStep)")
    }

    // MARK: - Input / Keys

    func controlTextDidChange(_ obj: Notification) {
        let typed = field.stringValue
        if session.triggerWord.isEmpty, let parsed = triggerSplit(typed) {
            session.query = parsed.0 + " " + parsed.1
            field.stringValue = parsed.1
        } else if !session.triggerWord.isEmpty {
            session.query = session.triggerWord + " " + typed
        } else {
            session.query = typed
        }
        session.search()
        Log.info("输入变化，长度 \(typed.count)，状态 \(session.machine.state)，结果 \(session.hits.count)")
        rebuildLauncher()
    }

    private func handleLauncherKey(_ event: NSEvent) -> Bool {
        // 输入框未聚焦时，先抢焦点，普通字符交给输入框
        let editing = launcher.firstResponder === field || launcher.firstResponder === field.currentEditor()
        if !editing {
            focusSearchField(reason: "按键补焦")
        }
        switch event.keyCode {
        case 125:
            guard !session.hits.isEmpty else { return true }
            session.selection = min(session.hits.count - 1, session.selection + 1)
            rebuildLauncher()
            return true
        case 126:
            guard !session.hits.isEmpty else { return true }
            session.selection = max(0, session.selection - 1)
            rebuildLauncher()
            return true
        case 36, 76:
            if !session.triggerWord.isEmpty, let hit = session.hits.first {
                session.execute(hit: hit)
            } else {
                session.executeSelection()
            }
            return true
        case 53:
            if toolWindow?.isVisible == true {
                closeTool()
                return true
            }
            if !session.triggerWord.isEmpty {
                dropTrigger()
                return true
            }
            if !session.query.isEmpty {
                clearQuery()
                return true
            }
            session.escape()
            return true
        case 48:
            // Tab：打开工具面板（原型快捷键），不交给输入框
            showTools()
            return true
        default:
            // 输入框中的 1–9 进入搜索。按住 ⌥ 再按数字，才打开对应的最近使用或结果。
            let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            if flags.contains(.option), let digit = digit(event) {
                let source = (session.query.isEmpty && session.triggerWord.isEmpty) ? homeList : session.hits
                if source.indices.contains(digit - 1) {
                    Log.info("⌥+\(digit) 打开第 \(digit) 条")
                    session.execute(hit: source[digit - 1])
                }
                return true
            }
            return false
        }
    }

    private func handleToolKey(_ event: NSEvent) -> Bool {
        switch event.keyCode {
        case 53:
            closeTool()
            return true
        case 36, 76:
            copyToolResult()
            return true
        default:
            return false
        }
    }

    // MARK: - Actions

    @objc private func launcherResigned() {
        guard session.settings.blurHide, launcher.isVisible, actionMenu == nil else { return }
        blurWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self, !self.launcher.isKeyWindow else { return }
            self.session.hide()
        }
        blurWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15, execute: work)
    }

    @objc private func hideFromButton() { session.hide() }
    @objc private func showSettingsFromLauncher() { showSettings() }
    @objc private func showToolsFromLauncher() { showTools() }
    @objc private func closeGroups() { dismissGroups() }

    private func dismissGroups() {
        rememberPanelFrame(groupsWindow)
        groupsWindow?.orderOut(nil)
    }
    @objc private func closeSettings() { settingsWindow?.orderOut(nil) }
    @objc private func noop() {}

    @objc private func clearQuery() {
        field.stringValue = ""
        session.query = ""
        session.triggerWord = ""
        session.payload = ""
        session.search()
        rebuildLauncher()
        Log.info("已清空输入")
    }

    @objc private func dropTrigger() {
        session.triggerWord = ""
        session.payload = ""
        session.query = ""
        field.stringValue = ""
        session.search()
        rebuildLauncher()
        Log.info("退出触发词胶囊")
    }

    @objc private func browse() {
        let q = session.query.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? ""
        if let url = URL(string: "https://www.bing.com/search?q=\(q)") { NSWorkspace.shared.open(url) }
        session.hide()
    }

    @objc private func chipClipboard() { showTool("clipboard") }
    @objc private func chipCalc() { showTool("calc") }
    @objc private func chipTranslate() { showTool("translate") }

    @objc private func demoQuery(_ sender: NSButton) {
        let q = sender.identifier?.rawValue ?? sender.title
        session.query = q
        session.search()
        rebuildLauncher()
        Log.info("演示查询 \(q)")
    }

    @objc private func openTriggerTool() {
        if let id = session.resolveTrigger(session.triggerWord)?.toolId {
            showTool(id)
        }
    }

    @objc private func copyTriggerResult() {
        if let text = session.hits.first?.copyText {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(text, forType: .string)
            Log.info("已复制触发词结果")
            session.hide()
        }
    }

    private func showItemMenu(_ hit: Hit, _ event: NSEvent, in view: NSView) {
        guard !hit.path.isEmpty else {
            Log.warn("这一条没有路径，不打开右键菜单")
            return
        }
        blurWork?.cancel()
        let menu = NSMenu()
        menu.delegate = self
        menu.addItem(actionItem("打开文件夹", #selector(openFolderItem(_:)), hit))
        menu.addItem(actionItem("复制路径", #selector(copyPath(_:)), hit))
        actionMenu = menu
        let point = view.convert(event.locationInWindow, from: nil)
        Log.info("右键菜单 \(hit.title)")
        menu.popUp(positioning: nil, at: point, in: view)
    }

    @objc private func openFolderItem(_ sender: NSMenuItem) {
        guard let hit = (sender.representedObject as? HitBox)?.hit, !hit.path.isEmpty else {
            Log.warn("打开文件夹失败，没有路径")
            return
        }
        let url = URL(fileURLWithPath: hit.path)
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: hit.path, isDirectory: &isDir) else {
            Log.warn("路径不存在 \(hit.path)")
            return
        }
        let folder = (isDir.boolValue && url.pathExtension != "app") ? url : url.deletingLastPathComponent()
        NSWorkspace.shared.open(folder)
        Log.info("打开文件夹 \(folder.path)")
    }

    func menuDidClose(_ menu: NSMenu) {
        actionMenu = nil
        guard let text = pendingCopy else { return }
        pendingCopy = nil
        DispatchQueue.main.async { [weak self] in
            self?.commitCopy(text)
        }
    }

    private func actionItem(_ title: String, _ action: Selector, _ hit: Hit) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        item.representedObject = HitBox(hit)
        return item
    }

    @objc private func copyPath(_ sender: NSMenuItem) {
        guard let hit = (sender.representedObject as? HitBox)?.hit else {
            Log.warn("复制路径失败，菜单项没有结果")
            return
        }
        pendingCopy = hit.path.isEmpty ? hit.title : hit.path
        Log.info("准备复制路径 \(pendingCopy ?? "")")
    }

    private func commitCopy(_ text: String) {
        let board = NSPasteboard.general
        board.declareTypes([.string], owner: nil)
        let ok = board.setString(text, forType: .string)
        let read = board.string(forType: .string) ?? ""
        Log.info(ok && read == text ? "已复制路径 \(text)" : "复制路径未留在剪贴板，读回 \(read)")
        footerHints.isHidden = true
        footerLeft.isHidden = false
        footerLeft.stringValue = "已复制 \(text)"
        footerLeft.lineBreakMode = .byTruncatingMiddle
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { [weak self] in
            self?.layoutFooter()
        }
    }

    @objc private func closeTool() {
        if toolMode == "page" {
            openToolGrid()
            return
        }
        toolWindow?.orderOut(nil)
        session.escape()
        if session.machine.state != "HIDDEN" { showLauncher() }
    }

    @objc private func copyToolResult() {
        let text = lastToolOutput.isEmpty ? (session.hits.first?.copyText ?? "") : lastToolOutput
        guard !text.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
        Log.info("已复制工具结果")
    }

    @objc private func togglePanelEdit() {
        panelEdit.toggle()
        renderGroups()
    }

    @objc private func toggleFold(_ sender: NSButton) {
        let id = sender.identifier?.rawValue ?? ""
        if foldedGroups.contains(id) { foldedGroups.remove(id) } else { foldedGroups.insert(id) }
        Log.info("分组 \(id) \(foldedGroups.contains(id) ? "已折叠" : "已展开")")
        renderGroups()
    }

    @objc private func panelSearchChanged(_ sender: NSTextField) {
        let next = sender.stringValue
        guard next != panelSearch else { return }
        panelSearch = next
        Log.info("快捷项搜索「\(next)」")
        renderGroups()
    }

    private func confirmDeleteGroup(id: String) {
        guard id != "all", id != "ungrouped" else { return }
        guard let group = session.library.groups.first(where: { $0.id == id }) else { return }
        let body = session.store.text("group.deleteConfirm", ["name": group.name, "count": "\(group.itemIds.count)"])
        let alert = NSAlert()
        alert.messageText = session.store.text("group.deleteTitle")
        alert.informativeText = body
        alert.alertStyle = .warning
        alert.addButton(withTitle: "删除")
        alert.addButton(withTitle: "取消")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        session.deleteGroup(id)
        if panelNav == id {
            panelNav = "all"
        }
        renamingGroupId = nil
        panelEdit = false
        renderGroups()
    }

    @objc private func addGroup() {
        let colors = session.store.groupColors
        let colorHex = colors.indices.contains(3) ? colors[3] : "#9B6CF6"
        let id = "g\(Int(Date().timeIntervalSince1970))"
        session.library.groups.append(Group(id: id, name: "新分组", color: colorHex, itemIds: []))
        session.save()
        panelNav = id
        renderGroups()
        Log.info("新建分组 \(id) 色 \(colorHex)")
    }

    @objc private func commitGroupName(_ sender: NSTextField) {
        guard !renamingCommit else { return }
        renamingCommit = true
        defer { renamingCommit = false }
        let raw = sender.identifier?.rawValue ?? ""
        let id = raw.hasPrefix("rename-") ? String(raw.dropFirst("rename-".count)) : raw
        let name = sender.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        renamingGroupId = nil
        if name.isEmpty {
            Log.warn("分组名称不能为空")
            renderGroups()
            return
        }
        session.renameGroup(id, to: name)
        renderGroups()
    }

    @objc private func addShortcut() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = true
        panel.treatsFilePackagesAsDirectories = false
        panel.prompt = "添加"
        panel.message = "选择应用、文件或文件夹"
        presentFilePanel(panel, over: groupsWindow) { [weak self] response in
            guard response == .OK, let self else { return }
            let groupId = self.dropTarget(hover: nil)
            for url in panel.urls { self.session.addShortcut(url: url, groupId: groupId) }
            self.renderGroups()
        }
    }

    private func openShortcut(_ item: Shortcut) {
        if item.kind == "url", let url = URL(string: item.path), let scheme = url.scheme?.lowercased(), scheme == "http" || scheme == "https" {
            NSWorkspace.shared.open(url)
            Log.info("打开网址快捷项 \(item.title)")
            return
        }
        if !FileManager.default.fileExists(atPath: item.path) {
            Log.warn("快捷项已经不在原位置 \(item.path)")
        }
        let opened = NSWorkspace.shared.open(URL(fileURLWithPath: item.path))
        Log.info(opened ? "打开快捷项 \(item.title)" : "打开失败 \(item.path)")
    }

    private func installPanelDrop(_ root: PanelDropView) {
        let veil = PassThroughView(frame: root.bounds)
        veil.identifier = NSUserInterfaceItemIdentifier("drop-veil")
        veil.isHidden = true
        veil.wantsLayer = true
        veil.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.28).cgColor
        let badge = FlippedView(frame: NSRect(x: (root.bounds.width - 380) / 2, y: (root.bounds.height - 92) / 2, width: 380, height: 92))
        badge.wantsLayer = true
        badge.layer?.backgroundColor = color("bg.float").cgColor
        badge.layer?.cornerRadius = 12
        badge.layer?.borderWidth = 1
        badge.layer?.borderColor = color("accent").cgColor
        let title = UI.label("松手添加到分组", 16, .semibold, color("text.primary"))
        title.identifier = NSUserInterfaceItemIdentifier("drop-title")
        title.alignment = .center
        title.frame = NSRect(x: 16, y: 22, width: 348, height: 24)
        let sub = UI.label("应用、文件、文件夹或网址", 12, .regular, color("text.secondary"))
        sub.alignment = .center
        sub.frame = NSRect(x: 16, y: 50, width: 348, height: 18)
        badge.addSubview(title)
        badge.addSubview(sub)
        veil.addSubview(badge)
        root.addSubview(veil)

        root.onUpdate = { [weak self, weak root] info in
            guard let self, let root else { return [] }
            let operation = self.dropOperation(info)
            guard !operation.isEmpty else { return [] }
            let groupId = self.dropTarget(hover: self.groupUnderCursor(info, in: root))
            self.showDropHint(groupId, in: root)
            return operation
        }
        root.onExit = { [weak self, weak root] in
            guard let self, let root else { return }
            self.hideDropHint(in: root)
        }
        root.onPerform = { [weak self, weak root] info in
            guard let self, let root else { return false }
            let urls = self.droppedItems(info)
            guard !urls.isEmpty else {
                let types = (info.draggingPasteboard.types ?? []).map(\.rawValue).joined(separator: ",")
                Log.warn("松手后没有读到文件或文件夹 类型=\(types)")
                return false
            }
            let groupId = self.dropTarget(hover: self.groupUnderCursor(info, in: root))
            var added = 0
            for url in urls where self.session.addShortcut(url: url, groupId: groupId) {
                added += 1
            }
            let name = self.session.library.groups.first { $0.id == groupId }?.name ?? groupId
            Log.info("拖入完成，放入「\(name)」\(added) 个，共 \(urls.count) 个")
            DispatchQueue.main.async { [weak self] in self?.renderGroups() }
            return true
        }
        releaseInnerDropClaims(root)
        root.registerForDraggedTypes(PanelDropView.dragTypes)
        root.needsDisplay = true
        Log.info("快捷启动已准备接收拖拽")
        DispatchQueue.main.async { [weak self, weak root] in
            guard let self, let root else { return }
            self.releaseInnerDropClaims(root)
            root.registerForDraggedTypes(PanelDropView.dragTypes)
        }
    }

    /// 输入框和滚动区会自己登记拖放，抢先拒绝文件。清掉之后才会落到外层面板。
    private func releaseInnerDropClaims(_ view: NSView) {
        for child in view.subviews {
            if !(child is PanelDropView) { child.unregisterDraggedTypes() }
            releaseInnerDropClaims(child)
        }
    }

    private func dropOperation(_ info: NSDraggingInfo) -> NSDragOperation {
        guard acceptsDrop(info) else { return [] }
        let mask = info.draggingSourceOperationMask
        if mask.contains(.copy) { return .copy }
        if mask.contains(.link) { return .link }
        if mask.contains(.generic) { return .generic }
        return .copy
    }

    private func acceptsDrop(_ info: NSDraggingInfo) -> Bool {
        let raw = (info.draggingPasteboard.types ?? []).map(\.rawValue).joined(separator: " ").lowercased()
        if raw.contains("file") || raw.contains("url") || raw.contains("filename") { return true }
        return !droppedItems(info).isEmpty
    }

    private func droppedItems(_ info: NSDraggingInfo) -> [URL] {
        let board = info.draggingPasteboard
        var urls: [URL] = []
        func append(_ url: URL?) {
            guard let url else { return }
            if urls.contains(where: { $0.absoluteString == url.absoluteString }) { return }
            urls.append(url)
        }
        func appendString(_ value: String?) {
            guard let value else { return }
            let text = value.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !text.isEmpty else { return }
            if text.hasPrefix("file:") || text.hasPrefix("http://") || text.hasPrefix("https://") {
                append(URL(string: text))
            } else if text.hasPrefix("/") {
                append(URL(fileURLWithPath: text))
            }
        }
        for item in board.pasteboardItems ?? [] {
            appendString(item.string(forType: .fileURL))
            appendString(item.string(forType: .URL))
            appendString(item.string(forType: NSPasteboard.PasteboardType("public.file-url")))
            appendString(item.string(forType: .string))
        }
        if urls.isEmpty, let objects = board.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: NSNumber(value: true)]) {
            for case let url as URL in objects { append(url) }
        }
        if urls.isEmpty, let objects = board.readObjects(forClasses: [NSURL.self], options: nil) {
            for case let url as URL in objects {
                let scheme = url.scheme?.lowercased() ?? ""
                if url.isFileURL || scheme == "http" || scheme == "https" { append(url) }
            }
        }
        if let names = board.propertyList(forType: NSPasteboard.PasteboardType("NSFilenamesPboardType")) as? [String] {
            for name in names { appendString(name) }
        }
        return urls
    }

    private func groupUnderCursor(_ info: NSDraggingInfo, in root: NSView) -> String? {
        let point = root.convert(info.draggingLocation, from: nil)
        var view = root.hitTest(point)
        while let current = view {
            if let raw = current.identifier?.rawValue, raw.hasPrefix("group-") {
                return String(raw.dropFirst("group-".count))
            }
            view = current.superview
        }
        return nil
    }

    private func dropTarget(hover: String?) -> String {
        if let hover, session.library.groups.contains(where: { $0.id == hover }) {
            return hover
        }
        if panelNav != "all", session.library.groups.contains(where: { $0.id == panelNav }) {
            return panelNav
        }
        if session.library.groups.contains(where: { $0.id == "ungrouped" }) { return "ungrouped" }
        return session.library.groups.first?.id ?? "ungrouped"
    }

    private func showDropHint(_ groupId: String, in root: NSView) {
        guard let veil = root.find(id: "drop-veil") else { return }
        veil.isHidden = false
        let name = session.library.groups.first { $0.id == groupId }?.name ?? "未分组"
        if let title = veil.find(id: "drop-title") as? NSTextField {
            title.stringValue = "松手添加到「\(name)」"
        }
        highlightGroup(groupId, in: root)
    }

    private func hideDropHint(in root: NSView) {
        root.find(id: "drop-veil")?.isHidden = true
        highlightGroup(nil, in: root)
    }

    private func highlightGroup(_ id: String?, in root: NSView) {
        let next = id.flatMap { root.find(id: "group-\($0)") }
        if next === dropHighlight { return }
        dropHighlight?.layer?.borderColor = color("border.default").cgColor
        dropHighlight?.layer?.borderWidth = 1
        next?.layer?.borderColor = color("accent").cgColor
        next?.layer?.borderWidth = 2
        dropHighlight = next
    }

    @objc private func deleteShortcut(_ sender: NSButton) {
        let id = sender.identifier?.rawValue ?? ""
        removeShortcut(id: id)
    }

    private func removeShortcut(id: String) {
        guard !id.isEmpty else { return }
        session.library.items.removeAll { $0.id == id }
        for i in session.library.groups.indices {
            session.library.groups[i].itemIds.removeAll { $0 == id }
        }
        session.save()
        renderGroups()
    }

    @objc private func togglePause(_ sender: SwitchView) {
        session.settings.clipboardPaused = sender.on
        session.save()
        Log.info("剪贴板记录 \(sender.on ? "已暂停" : "已恢复")")
    }

    @objc private func togglePauseFromButton() {
        session.settings.clipboardPaused.toggle()
        session.save()
        Log.info("剪贴板记录 \(session.settings.clipboardPaused ? "已暂停" : "已恢复")")
        renderSettings()
    }

    @objc private func toggleCloud(_ sender: SwitchView) {
        session.settings.cloudSync = sender.on
        session.save()
        Log.info(sender.on ? "已记下云同步开关。当前版本数据仍只在本机" : "云同步保持关闭")
    }

    @objc private func toggleUpdate(_ sender: SwitchView) {
        session.settings.autoUpdate = sender.on
        session.save()
        Log.info("主程序更新检查 \(sender.on ? "开启" : "关闭")")
    }

    @objc private func pickSummonMode(_ sender: NSButton) {
        pickMenu(sender, [
            ("hotkey", "全局快捷键"),
            ("menu", "菜单栏"),
            ("edge", "仅从菜单打开")
        ]) { value in
            self.session.settings.summonMode = value
            self.session.save()
            Log.info("呼出方式 \(value)")
            self.renderSettings()
        }
    }

    @objc private func pickMultiScreen(_ sender: NSButton) {
        pickMenu(sender, [
            ("followCursor", "跟随鼠标所在屏"),
            ("last", "记住上次呼出屏"),
            ("main", "固定主屏")
        ]) { value in
            self.session.settings.multiScreen = value
            self.session.save()
            Log.info("多屏行为 \(value)")
            self.renderSettings()
        }
    }

    @objc private func pickRetention(_ sender: NSButton) {
        pickMenu(sender, [("7d", "7 天"), ("30d", "30 天"), ("forever", "永久")]) { value in
            self.session.settings.clipboardRetention = value
            self.session.save()
            self.session.pruneClips()
            Log.info("剪贴板保留 \(value)")
            self.renderSettings()
        }
    }

    @objc private func pickTheme(_ sender: NSButton) {
        session.settings.theme = sender.identifier?.rawValue ?? "light"
        session.save()
        applyAppearance()
        Log.info("主题 \(session.settings.theme)")
        renderSettings()
    }

    @objc private func pickAccent(_ sender: NSButton) {
        session.settings.accentToken = sender.identifier?.rawValue ?? "accent"
        session.save()
        Log.info("强调色 \(session.settings.accentToken)")
        renderSettings()
    }

    @objc private func pickDensity(_ sender: NSButton) {
        session.settings.density = sender.identifier?.rawValue ?? "standard"
        session.save()
        Log.info("结果密度 \(session.settings.density)")
        renderSettings()
    }

    @objc private func manageData(_ sender: NSButton) {
        pickMenu(sender, [("open", "打开数据目录"), ("export", "导出数据"), ("clear", "清空剪贴板")]) { value in
            switch value {
            case "open":
                NSWorkspace.shared.open(Paths.root)
            case "export":
                self.exportData()
            default:
                self.clearClipboardHistory()
            }
        }
    }

    @objc private func clearClipboardHistory() {
        session.clips = []
        session.save()
        Log.info("已清空剪贴板历史")
        renderSettings()
    }

    @objc private func checkUpdate(_ sender: NSButton) {
        let message = session.settings.autoUpdate ? "当前已是 DeskKit 0.9.0，没有新版本。" : "自动检查已关闭。当前版本仍是 DeskKit 0.9.0。"
        Log.info(message)
        let alert = NSAlert()
        alert.messageText = "检查更新"
        alert.informativeText = message
        alert.addButton(withTitle: "好")
        alert.runModal()
    }

    @objc private func replayGuide(_ sender: NSButton) {
        session.settings.guideDone = false
        session.save()
        Log.info("重新打开引导")
        showGuide()
    }

    @objc private func askListen() {
        if CGPreflightListenEventAccess() {
            Log.info("输入监控已经授予")
            return
        }
        CGRequestListenEventAccess()
        Log.info("已请求输入监控权限")
    }

    @objc private func saveTrigger(_ sender: NSTextField) {
        let toolId = sender.identifier?.rawValue ?? ""
        let word = sender.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !toolId.isEmpty, !word.isEmpty else { return }
        let clash = session.settings.triggerWords.first { $0.key != toolId && $0.value.caseInsensitiveCompare(word) == .orderedSame }
            ?? session.store.triggers.first { $0.toolId != toolId && $0.word.caseInsensitiveCompare(word) == .orderedSame && session.settings.triggerWords[$0.toolId] == nil }.map { ($0.toolId, $0.word) }.map { (key: $0.0, value: $0.1) }
        if clash != nil {
            Log.warn("触发词 \(word) 已被占用，没有保存")
            renderSettings()
            return
        }
        session.settings.triggerWords[toolId] = word
        session.save()
        Log.info("触发词 \(toolId) 改为 \(word)")
    }

    @objc private func beginRecord(_ sender: NSButton) {
        recordingHotkey = sender.identifier?.rawValue
        Log.info("开始录制快捷键 \(recordingHotkey ?? "")")
        renderSettings()
    }

    @objc private func resetHotkey(_ sender: NSButton) {
        let id = sender.identifier?.rawValue ?? ""
        session.settings.hotkeys.removeValue(forKey: id)
        if id == "summon" {
            session.settings.summonKeyCode = 49
            session.settings.summonAlt = true
            session.settings.summonCommand = false
            session.settings.summonControl = false
            session.settings.summonShift = false
        }
        session.save()
        onHotkeyChanged()
        Log.info("已恢复默认快捷键 \(id)")
        renderSettings()
    }

    private func captureHotkey(_ event: NSEvent) {
        guard let id = recordingHotkey else { return }
        if event.keyCode == 53 {
            recordingHotkey = nil
            Log.info("取消录制快捷键")
            renderSettings()
            return
        }
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let alt = flags.contains(.option)
        let command = flags.contains(.command)
        let control = flags.contains(.control)
        let shift = flags.contains(.shift)
        guard alt || command || control || shift else {
            Log.warn("录制快捷键需要至少一个修饰键")
            return
        }
        let binding = HotkeyBinding(
            keyCode: Int(event.keyCode),
            alt: alt,
            command: command,
            control: control,
            shift: shift,
            text: hotkeyLabel(alt: alt, command: command, control: control, shift: shift, keyCode: event.keyCode, characters: event.charactersIgnoringModifiers)
        )
        if let other = clashingHotkey(binding, except: id) {
            Log.warn("快捷键 \(binding.text) 与 \(other) 冲突，没有保存")
            return
        }
        session.settings.hotkeys[id] = binding
        if id == "summon" {
            session.settings.summonKeyCode = binding.keyCode
            session.settings.summonAlt = binding.alt
            session.settings.summonCommand = binding.command
            session.settings.summonControl = binding.control
            session.settings.summonShift = binding.shift
        }
        session.save()
        recordingHotkey = nil
        onHotkeyChanged()
        Log.info("已录制 \(id) \(binding.text)")
        renderSettings()
    }

    private func exportData() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.prompt = "导出到这里"
        presentFilePanel(panel, over: settingsWindow) { response in
            guard response == .OK, let folder = panel.url else { return }
            let destination = folder.appendingPathComponent("DeskKit-export")
            try? FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
            for name in ["settings.json", "groups.json", "clipboard.json", "recents.json"] {
                let source = Paths.file(name)
                let target = destination.appendingPathComponent(name)
                if FileManager.default.fileExists(atPath: target.path) {
                    try? FileManager.default.removeItem(at: target)
                }
                try? FileManager.default.copyItem(at: source, to: target)
            }
            Log.info("已导出数据到 \(destination.path)")
            NSWorkspace.shared.open(destination)
        }
    }

    func applyAppearance() {
        switch session.settings.theme {
        case "dark":
            NSApp.appearance = NSAppearance(named: .darkAqua)
        case "system":
            NSApp.appearance = nil
        default:
            NSApp.appearance = NSAppearance(named: .aqua)
        }
        Log.info("外观已应用 \(session.settings.theme)，深色 \(prefersDark)")
        refreshOpenSurfaces()
    }

    private func applyBackground(to root: NSView?) {
        guard let root else { return }
        let backdrop: NSImageView
        if let existing = root.subviews.first(where: { $0.identifier?.rawValue == "deskkit-background" }) as? NSImageView {
            backdrop = existing
        } else {
            backdrop = NSImageView(frame: root.bounds)
            backdrop.identifier = NSUserInterfaceItemIdentifier("deskkit-background")
            backdrop.imageScaling = .scaleAxesIndependently
            backdrop.imageAlignment = .alignCenter
            backdrop.wantsLayer = true
            backdrop.layer?.contentsGravity = .resizeAspectFill
            root.addSubview(backdrop, positioned: .below, relativeTo: nil)
        }
        backdrop.frame = root.bounds
        backdrop.image = nil
        backdrop.layer?.contentsGravity = .resizeAspectFill
        let alpha = CGFloat(min(1, max(0.15, session.settings.backgroundOpacity)))
        if let image = loadedBackground() {
            backdrop.layer?.contents = image
            backdrop.alphaValue = alpha
            root.layer?.backgroundColor = color("bg.float").cgColor
        } else {
            backdrop.layer?.contents = nil
            backdrop.alphaValue = 0
            root.layer?.backgroundColor = color("bg.float").withAlphaComponent(alpha).cgColor
        }
    }

    private func loadedBackground() -> NSImage? {
        let path = session.settings.backgroundImagePath
        if path != backgroundLoadedPath {
            backgroundLoadedPath = path
            backgroundImage = path.isEmpty ? nil : NSImage(contentsOfFile: path)
            if backgroundImage == nil, !path.isEmpty {
                Log.warn("背景图无法读取 \(path)")
            }
        }
        return backgroundImage
    }

    private func backgroundButtons() -> NSView {
        let box = FlippedView(frame: NSRect(x: 0, y: 0, width: 168, height: 28))
        let choose = softButton(session.settings.backgroundImagePath.isEmpty ? "选择图片" : "更换图片", #selector(chooseBackground))
        choose.frame = NSRect(x: 0, y: 0, width: 84, height: 28)
        let clear = softButton("清除", #selector(clearBackground))
        clear.frame = NSRect(x: 90, y: 0, width: 72, height: 28)
        box.addSubview(choose)
        box.addSubview(clear)
        return box
    }

    private func opacityControl() -> NSView {
        let box = FlippedView(frame: NSRect(x: 0, y: 0, width: 196, height: 28))
        let slider = NSSlider(value: session.settings.backgroundOpacity, minValue: 0.15, maxValue: 1, target: self, action: #selector(opacityChanged(_:)))
        slider.isContinuous = true
        slider.frame = NSRect(x: 0, y: 4, width: 140, height: 20)
        let label = UI.label("\(Int(session.settings.backgroundOpacity * 100))%", 12, .semibold, color("text.secondary"))
        label.alignment = .right
        label.frame = NSRect(x: 144, y: 5, width: 48, height: 18)
        label.identifier = NSUserInterfaceItemIdentifier("opacity-label")
        box.addSubview(slider)
        box.addSubview(label)
        return box
    }

    @objc private func opacityChanged(_ sender: NSSlider) {
        session.settings.backgroundOpacity = sender.doubleValue
        session.save()
        if let label = sender.superview?.subviews.first(where: { $0.identifier?.rawValue == "opacity-label" }) as? NSTextField {
            label.stringValue = "\(Int(sender.doubleValue * 100))%"
        }
        applyBackground(to: launcher.contentView)
        applyBackground(to: groupsWindow?.contentView)
        applyBackground(to: settingsWindow?.contentView)
        applyBackground(to: toolWindow?.contentView)
        footerBar.layer?.backgroundColor = color("bg.subtle").withAlphaComponent(CGFloat(sender.doubleValue)).cgColor
    }

    @objc private func chooseBackground() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.image]
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.prompt = "设为背景"
        presentFilePanel(panel, over: settingsWindow) { [weak self] response in
            guard response == .OK, let self, let url = panel.url else { return }
            let ext = url.pathExtension.isEmpty ? "png" : url.pathExtension
            let destination = Paths.root.appendingPathComponent("background.\(ext)")
            if FileManager.default.fileExists(atPath: destination.path) {
                try? FileManager.default.removeItem(at: destination)
            }
            do {
                try FileManager.default.copyItem(at: url, to: destination)
                self.session.settings.backgroundImagePath = destination.path
                self.backgroundLoadedPath = ""
                self.session.save()
                Log.info("已设置背景图 \(destination.lastPathComponent)")
                self.refreshOpenSurfaces()
            } catch {
                Log.warn("背景图复制失败 \(error.localizedDescription)")
            }
        }
    }

    private func presentFilePanel(_ panel: NSOpenPanel, over host: NSWindow?, _ completion: @escaping (NSApplication.ModalResponse) -> Void) {
        blurWork?.cancel()
        let parent = host ?? NSApp.keyWindow
        launcher.level = .normal
        for window in [groupsWindow, settingsWindow, toolWindow] {
            window?.level = window === parent ? .floating : .normal
        }
        let finish: (NSApplication.ModalResponse) -> Void = { [weak self] response in
            self?.launcher.level = .floating
            self?.groupsWindow?.level = .floating
            self?.settingsWindow?.level = .floating
            self?.toolWindow?.level = .floating
            completion(response)
        }
        if let parent {
            Log.info("文件选择框显示在 \(parent.title) 之上")
            parent.orderFrontRegardless()
            panel.beginSheetModal(for: parent, completionHandler: finish)
        } else {
            panel.level = .popUpMenu
            Log.info("文件选择框独立显示")
            panel.begin(completionHandler: finish)
            panel.orderFrontRegardless()
        }
    }

    @objc private func clearBackground() {
        if !session.settings.backgroundImagePath.isEmpty {
            try? FileManager.default.removeItem(atPath: session.settings.backgroundImagePath)
        }
        session.settings.backgroundImagePath = ""
        backgroundLoadedPath = ""
        backgroundImage = nil
        session.save()
        Log.info("已清除背景图")
        refreshOpenSurfaces()
    }

    private func refreshOpenSurfaces() {
        launcher.contentView?.layer?.backgroundColor = color("bg.float").cgColor
        footerBar.layer?.backgroundColor = color("bg.subtle").withAlphaComponent(CGFloat(session.settings.backgroundOpacity)).cgColor
        field.textColor = color("text.primary")
        searchIcon.image = AppIcons.symbol("magnifyingglass", size: 16, tint: color("text.tertiary"))
        if let gear = launcherRoot.subviews.first(where: { $0.identifier?.rawValue == "chrome-gear" }) as? NSButton {
            gear.image = AppIcons.symbol("gearshape", size: 15, tint: color("text.secondary"))
        }
        if let close = launcherRoot.subviews.first(where: { $0.identifier?.rawValue == "chrome-close" }) as? NSButton {
            close.image = AppIcons.symbol("xmark", size: 15, tint: color("text.secondary"))
        }
        clearButton.contentTintColor = color("text.tertiary")
        applyBackground(to: launcher.contentView)
        if launcher.isVisible { rebuildLauncher() }
        if settingsWindow?.isVisible == true { renderSettings() }
        if groupsWindow?.isVisible == true { renderGroups() }
        if toolWindow?.isVisible == true {
            if toolMode == "page", !activeTool.isEmpty { openToolPage(activeTool) }
            else { openToolGrid() }
        }
    }

    private func summonModeTitle() -> String {
        switch session.settings.summonMode {
        case "menu": return "菜单栏"
        case "edge": return "仅从菜单打开"
        default: return "全局快捷键"
        }
    }

    private func multiScreenTitle() -> String {
        switch session.settings.multiScreen {
        case "last": return "记住上次呼出屏"
        case "main": return "固定主屏"
        default: return "跟随鼠标所在屏"
        }
    }

    private func retentionTitle() -> String {
        switch session.settings.clipboardRetention {
        case "30d": return "30 天"
        case "forever": return "永久"
        default: return "7 天"
        }
    }

    private func hotkeyText(_ id: String) -> String {
        if let saved = session.settings.hotkeys[id] { return saved.text }
        return session.store.compactChord(platform: "macos", action: id)
    }

    private func clashingHotkey(_ binding: HotkeyBinding, except id: String) -> String? {
        if let other = session.settings.hotkeys.first(where: {
            $0.key != id && $0.value.keyCode == binding.keyCode && $0.value.alt == binding.alt
                && $0.value.command == binding.command && $0.value.control == binding.control && $0.value.shift == binding.shift
        }) {
            return other.key
        }
        if id != "summon",
           binding.keyCode == session.settings.summonKeyCode,
           binding.alt == session.settings.summonAlt,
           binding.command == session.settings.summonCommand,
           binding.control == session.settings.summonControl,
           binding.shift == session.settings.summonShift {
            return "summon"
        }
        return nil
    }

    private func hotkeyConflict(_ id: String) -> String? {
        guard let mine = session.settings.hotkeys[id] else { return nil }
        if let other = session.settings.hotkeys.first(where: { $0.key != id && $0.value.text == mine.text }) {
            return "与\(other.key)冲突"
        }
        return nil
    }

    private func hotkeyLabel(alt: Bool, command: Bool, control: Bool, shift: Bool, keyCode: UInt16, characters: String?) -> String {
        var parts: [String] = []
        if control { parts.append(session.store.keyLabel(platform: "macos", key: "control")) }
        if alt { parts.append(session.store.keyLabel(platform: "macos", key: "alt")) }
        if shift { parts.append(session.store.keyLabel(platform: "macos", key: "shift")) }
        if command { parts.append(session.store.keyLabel(platform: "macos", key: "command")) }
        let named: [UInt16: String] = [49: "Space", 36: "⏎", 48: "Tab", 53: "Esc", 43: ",", 123: "←", 124: "→", 126: "↑", 125: "↓"]
        let key = named[keyCode] ?? (characters?.uppercased() ?? "?")
        parts.append(key)
        return parts.joined()
    }

    private func pickMenu(_ sender: NSButton, _ options: [(String, String)], _ apply: @escaping (String) -> Void) {
        let menu = NSMenu()
        for option in options {
            let item = NSMenuItem(title: option.1, action: #selector(runMenuChoice(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = MenuChoice(option.0, apply)
            menu.addItem(item)
        }
        let point = NSPoint(x: 0, y: sender.bounds.height + 4)
        menu.popUp(positioning: nil, at: point, in: sender)
    }

    @objc private func runMenuChoice(_ sender: NSMenuItem) {
        (sender.representedObject as? MenuChoice)?.run()
    }

    @objc private func toggleLaunch(_ sender: SwitchView) {
        session.settings.autoLaunch = sender.on
        do {
            if sender.on { try SMAppService.mainApp.register() }
            else { try SMAppService.mainApp.unregister() }
            Log.info("开机自启 \(sender.on)")
        } catch {
            Log.warn("开机自启设置失败 \(error.localizedDescription)")
        }
        session.save()
    }

    @objc private func toggleBlur(_ sender: SwitchView) {
        session.settings.blurHide = sender.on
        session.save()
        Log.info("失焦自动隐藏 \(sender.on)")
    }

    @objc private func calcChanged(_ sender: NSTextField) {
        let text = sender.stringValue
        let value = text.lowercased().contains(" to ")
            ? (UnitConverter.convert(text) ?? "—")
            : (ExpressionEvaluator.evaluate(text).ok ? ExpressionEvaluator.evaluate(text).data : "—")
        publishTool(value)
    }

    @objc private func useSample(_ sender: NSButton) {
        let text = sender.title
        let value = text.lowercased().contains(" to ")
            ? (UnitConverter.convert(text) ?? "—")
            : ExpressionEvaluator.evaluate(text).data
        publishTool(value)
    }

    @objc private func translateChanged(_ sender: NSTextField) {
        let result = Translator.translate(sender.stringValue)
        publishTool(result.text)
    }

    @objc private func convertChanged(_ sender: NSTextField) {
        let id = sender.identifier?.rawValue ?? ""
        if id == "timestamp" {
            publishTool(TimestampConvert.describe(sender.stringValue) ?? "格式非法")
        } else {
            let hex = colorHex(from: sender.stringValue) ?? "#CCCCCC"
            if let view = toolWindow?.contentView?.find(id: "color-swatch") {
                view.layer?.backgroundColor = Theme.paint(hex).cgColor
            }
            lastToolOutput = hex
            publishTool(ColorConvert.describe(sender.stringValue) ?? "格式非法")
        }
    }

    @objc private func tsNow() {
        let stamp = String(Int(Date().timeIntervalSince1970))
        publishTool(TimestampConvert.describe(stamp) ?? stamp)
        lastToolOutput = stamp
    }

    @objc private func copyClip(_ sender: NSButton) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(sender.identifier?.rawValue ?? "", forType: .string)
        Log.info("已复制剪贴板记录")
    }

    @objc private func clearClips() {
        session.clips = []
        session.save()
        openToolPage("clipboard")
        Log.info("已清空剪贴板历史")
    }

    @objc private func askScreen() {
        if !CGPreflightScreenCaptureAccess() { CGRequestScreenCaptureAccess() }
    }

    @objc private func pickColor() {
        if !CGPreflightScreenCaptureAccess() {
            Log.warn("屏幕录制未授权")
            CGRequestScreenCaptureAccess()
        }
        NSColorSampler().show { [weak self] color in
            guard let self else { return }
            guard let color else {
                Log.warn("取色没有返回颜色")
                return
            }
            let hex = String(format: "#%02X%02X%02X", Int((color.redComponent * 255).rounded()), Int((color.greenComponent * 255).rounded()), Int((color.blueComponent * 255).rounded()))
            self.lastToolOutput = hex
            self.publishTool(hex)
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(hex, forType: .string)
            Log.info("已复制取色 \(hex)")
        }
    }

    @objc private func guideNext() {
        if guideStep < 3 {
            guideStep += 1
            renderGuide()
        } else {
            guideSkip()
        }
    }

    @objc private func guidePrev() {
        guideStep = max(1, guideStep - 1)
        renderGuide()
    }

    @objc private func guideSkip() {
        session.settings.guideDone = true
        session.save()
        guideWindow?.orderOut(nil)
        session.summon()
        Log.info("引导完成")
    }

    @objc private func askAccess() {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        _ = AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
        _ = URL(string: "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_Accessibility").map { NSWorkspace.shared.open($0) }
    }

    @objc private func openData() { NSWorkspace.shared.open(Paths.root) }

    // MARK: - Helpers

    private func publishTool(_ text: String) {
        lastToolOutput = text
        toolWindow?.contentView?.find(id: "tool-output").flatMap { $0 as? NSTextField }?.stringValue = text
    }

    private func enter(_ next: String) {
        if session.machine.state == next { return }
        if session.machine.transition(next).ok { return }
        for bridge in ["TOOL", "A3", "A2", "A1"] where bridge != session.machine.state {
            guard session.machine.transition(bridge).ok else { continue }
            if session.machine.state == next || session.machine.transition(next).ok { return }
        }
        Log.warn("未能进入 \(next)，当前 \(session.machine.state)")
    }

    private func triggerSplit(_ text: String) -> (String, String)? {
        guard let space = text.firstIndex(of: " ") else { return nil }
        let word = String(text[..<space])
        guard session.resolveTrigger(word) != nil else { return nil }
        return (word, String(text[text.index(after: space)...]))
    }

    private func digit(_ event: NSEvent) -> Int? {
        let map: [UInt16: Int] = [18: 1, 19: 2, 20: 3, 21: 4, 23: 5, 22: 6, 26: 7, 28: 8, 25: 9]
        return map[event.keyCode]
    }

    private func toolTitle(_ id: String) -> String {
        Catalog.tools.first { $0.id == id }?.title ?? id
    }

    private func colorHex(from input: String) -> String? {
        guard let rgb = ColorConvert.parse(input) else { return nil }
        return String(format: "#%02X%02X%02X", rgb.0, rgb.1, rgb.2)
    }

    private func glyphColor(_ title: String) -> NSColor {
        let colors = session.store.groupColors
        let index = abs(title.hashValue) % max(colors.count, 1)
        return colors.isEmpty ? color("accent") : Theme.paint(colors[index])
    }

    private func memoryText() -> String {
        var info = mach_task_basic_info()
        var count = mach_msg_type_number_t(MemoryLayout<mach_task_basic_info>.size) / 4
        let result = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                task_info(mach_task_self_, task_flavor_t(MACH_TASK_BASIC_INFO), $0, &count)
            }
        }
        guard result == KERN_SUCCESS else { return "—" }
        let mb = Double(info.resident_size) / 1024 / 1024
        return String(format: "%.0f MB", mb)
    }

    private func pane(_ frame: NSRect, _ title: String) -> FlippedView {
        let box = FlippedView(frame: frame)
        box.wantsLayer = true
        box.layer?.backgroundColor = color("bg.float").cgColor
        box.layer?.cornerRadius = 10
        box.layer?.borderWidth = 1
        box.layer?.borderColor = color("border.default").cgColor
        let head = UI.label(title, 12, .semibold, color("text.tertiary"))
        head.frame = NSRect(x: 16, y: 14, width: frame.width - 32, height: 18)
        box.addSubview(head)
        return box
    }

    private func settingsBox(_ frame: NSRect, _ title: String) -> FlippedView {
        let box = FlippedView(frame: frame)
        box.wantsLayer = true
        box.layer?.backgroundColor = color("bg.float").cgColor
        box.layer?.cornerRadius = 10
        box.layer?.borderWidth = 1
        box.layer?.borderColor = color("border.default").cgColor
        if !title.isEmpty {
            let head = UI.label(title, 14, .semibold, color("text.primary"))
            head.frame = NSRect(x: 16, y: 14, width: 400, height: 20)
            box.addSubview(head)
        }
        return box
    }

    private func addSettingRow(_ parent: NSView, _ y: CGFloat, _ title: String, _ detail: String, _ control: NSView?) -> CGFloat {
        let titleLabel = UI.label(title, 13, .semibold, color("text.primary"))
        titleLabel.frame = NSRect(x: 16, y: y, width: 460, height: 18)
        let detailLabel = UI.label(detail, 12, .regular, color("text.secondary"))
        detailLabel.frame = NSRect(x: 16, y: y + 20, width: 460, height: 16)
        parent.addSubview(titleLabel)
        parent.addSubview(detailLabel)
        if let control {
            control.frame.origin = NSPoint(x: parent.bounds.width - control.frame.width - 20, y: y + 4)
            parent.addSubview(control)
        }
        return y + 52
    }

    private func makeSwitch(_ on: Bool, _ action: Selector) -> SwitchView {
        let toggle = SwitchView(frame: NSRect(x: 0, y: 0, width: 40, height: 24))
        toggle.on = on
        toggle.onColor = color("accent")
        toggle.target = self
        toggle.action = action
        return toggle
    }

    private func valueChip(_ text: String) -> NSTextField {
        let chip = UI.label(text, 12, .semibold, color("text.primary"))
        chip.alignment = .right
        chip.frame = NSRect(x: 0, y: 0, width: 180, height: 20)
        return chip
    }

    private func pillTag(_ text: String) -> NSTextField {
        let tag = UI.label(text, 11, .semibold, color("accent.hover"))
        tag.alignment = .center
        tag.wantsLayer = true
        tag.layer?.backgroundColor = color("accent.soft").cgColor
        tag.layer?.cornerRadius = 8
        tag.frame = NSRect(x: 0, y: 0, width: CGFloat(text.count) * 7 + 20, height: 22)
        return tag
    }

    private func primaryButton(_ title: String, _ action: Selector) -> NSButton {
        let button = SolidButton(title: title, target: self, action: action)
        button.isBordered = false
        button.wantsLayer = true
        button.layer?.backgroundColor = color("accent").cgColor
        button.layer?.cornerRadius = 8
        button.attributedTitle = NSAttributedString(string: title, attributes: [
            .foregroundColor: NSColor.white,
            .font: UI.font(13, .semibold)
        ])
        return button
    }

    private func softButton(_ title: String, _ action: Selector) -> NSButton {
        let button = SolidButton(title: title, target: self, action: action)
        button.isBordered = false
        button.wantsLayer = true
        button.layer?.backgroundColor = color("bg.subtle").cgColor
        button.layer?.cornerRadius = 8
        button.layer?.borderWidth = 1
        button.layer?.borderColor = color("border.default").cgColor
        button.attributedTitle = NSAttributedString(string: title, attributes: [
            .foregroundColor: color("text.secondary"),
            .font: UI.font(12, .medium)
        ])
        return button
    }

    private struct LauncherLayout {
        let width: CGFloat
        let pad: CGFloat
        let rowW: CGFloat
        let edge: CGFloat
    }

    private var scrollGutter: CGFloat { 8 }

    private func uiEdge() -> CGFloat { session.store.layout("launcher", "edgeInset") }

    private func panelContentWidth(_ canvas: NSView) -> CGFloat {
        max(0, canvas.bounds.width - scrollGutter)
    }

    private func launcherLayout() -> LauncherLayout {
        let width = session.store.layout("launcher", "width")
        let edge = uiEdge()
        let rowW = width - edge * 2
        return LauncherLayout(width: width, pad: edge, rowW: rowW, edge: edge)
    }

    private func layoutLauncherChrome() {
        let layout = launcherLayout()
        let width = layout.width
        let edge = layout.edge
        let inputRow = session.store.layout("launcher", "inputRowHeight")
        func mid(_ height: CGFloat) -> CGFloat { floor((inputRow - height) / 2) }

        let btn: CGFloat = 28
        let gap: CGFloat = 8
        let closeX = width - edge - btn
        let gearX = closeX - gap - btn
        let clearX = gearX - gap - btn
        if let close = launcherRoot.subviews.first(where: { $0.identifier?.rawValue == "chrome-close" }) {
            close.frame = NSRect(x: closeX, y: mid(btn), width: btn, height: btn)
        }
        if let gear = launcherRoot.subviews.first(where: { $0.identifier?.rawValue == "chrome-gear" }) {
            gear.frame = NSRect(x: gearX, y: mid(btn), width: btn, height: btn)
        }
        clearButton.frame = NSRect(x: clearX, y: mid(btn), width: btn, height: btn)

        let fieldY = mid(36)
        if capsuleSlot.frame.width > 0 {
            let fieldX = capsuleSlot.frame.maxX + 8
            field.frame = NSRect(x: fieldX, y: fieldY, width: max(80, clearButton.frame.minX - 8 - fieldX), height: 36)
        } else {
            field.frame = NSRect(x: 44, y: fieldY, width: max(80, clearButton.frame.minX - 8 - 44), height: 36)
        }

        footerBar.frame.size.width = width
        footerBrand.frame = NSRect(x: width - edge - 84, y: 8, width: 84, height: 16)
        footerHints.frame = NSRect(x: edge, y: 0, width: max(80, footerBrand.frame.minX - edge - 8), height: 32)
        if let footHit = footerBar.subviews.first(where: { $0.identifier?.rawValue == "hotkey-foot" }) {
            footHit.frame = NSRect(x: 0, y: 0, width: footerHints.frame.maxX, height: 32)
        }
    }

    private func layoutFooter() {
        footerHints.subviews.forEach { $0.removeFromSuperview() }
        let alt = altLabel()
        if !hotkeyReady {
            footerHints.isHidden = true
            footerLeft.isHidden = false
            footerLeft.stringValue = "\(alt)Space 未生效 · 点此打开辅助功能"
            return
        }
        footerHints.isHidden = false
        footerLeft.isHidden = true
        let up = session.store.keyLabel(platform: "macos", key: "up")
        let down = session.store.keyLabel(platform: "macos", key: "down")
        let items = [
            (up + down, "选择"),
            (session.store.keyLabel(platform: "macos", key: "enter"), "执行"),
            ("\(alt)1–9", "打开"),
            (session.store.keyLabel(platform: "macos", key: "tab"), "补全"),
            (session.store.keyLabel(platform: "macos", key: "esc"), "回退")
        ]
        var x: CGFloat = 0
        for item in items {
            let width = KeycapLabel.width(for: item.0)
            let cap = KeycapLabel(frame: NSRect(x: x, y: 7, width: width, height: 18))
            cap.text = item.0
            cap.tint = color("text.secondary")
            cap.fill = color("bg.input")
            cap.border = color("border.default")
            footerHints.addSubview(cap)
            x += width + 6
            let captionFont = UI.font(11)
            let labelW = PlainLabel.width(for: item.1, font: captionFont)
            let label = PlainLabel(frame: NSRect(x: x, y: 7, width: labelW, height: 18))
            label.text = item.1
            label.font = captionFont
            label.color = color("text.tertiary")
            footerHints.addSubview(label)
            x += labelW + 14
        }
    }

    private func altLabel() -> String {
        session.store.keyLabel(platform: "macos", key: "alt")
    }

    private func iconButton(_ title: String, _ action: Selector) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.isBordered = false
        button.font = UI.font(13, .medium)
        button.contentTintColor = color("text.secondary")
        return button
    }

    private func symbolButton(_ name: String, _ action: Selector, point: CGFloat = 15) -> NSButton {
        let button = SolidButton()
        button.isBordered = false
        button.imagePosition = .imageOnly
        button.imageScaling = .scaleProportionallyDown
        button.image = AppIcons.symbol(name, size: point, tint: color("text.secondary"))
        button.contentTintColor = color("text.secondary")
        button.target = self
        button.action = action
        return button
    }

    private func color(_ name: String) -> NSColor {
        let key = (name == "accent" && session.settings.accentToken != "accent") ? session.settings.accentToken : name
        return Theme.paint(session.store.color(key, dark: prefersDark))
    }

    private var prefersDark: Bool {
        switch session.settings.theme {
        case "dark":
            return true
        case "system":
            return NSApp.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        default:
            return false
        }
    }
    private func paint(_ view: NSView, _ name: String) {
        view.wantsLayer = true
        view.layer?.backgroundColor = color(name).cgColor
    }

    private func card(_ view: NSView) {
        view.wantsLayer = true
        view.layer?.cornerRadius = 14
        view.layer?.masksToBounds = true
    }

    private func makeWindow(_ title: String, _ size: NSSize) -> NSWindow {
        let window = KeyWindow(
            contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.borderless, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        window.title = title
        window.appearance = nil
        // 若开启，边框less 窗口会与侧栏自定义拖放排序抢手势。
        window.isMovableByWindowBackground = false
        window.backgroundColor = .clear
        window.isOpaque = false
        window.hasShadow = true
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.hidesOnDeactivate = false
        return window
    }

    private func present(_ window: NSWindow) {
        let fresh = !window.isVisible
        if fresh, window === groupsWindow {
            if !session.settings.panelPlacedByUser {
                placeNearMouse(window)
            }
        } else if fresh {
            place(window, topRatio: 0.12)
        }
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.hidesOnDeactivate = false
        window.level = .floating
        // 从菜单栏点开时，菜单还没关完就前置，窗口会跟着菜单一起被收掉。
        DispatchQueue.main.async {
            guard fresh || window.isVisible else { return }
            if fresh || !window.isKeyWindow {
                NSApp.activate(ignoringOtherApps: true)
                window.makeKeyAndOrderFront(nil)
            } else {
                window.orderFrontRegardless()
            }
            if fresh {
                Log.info("窗口已前置 \(window.title) 可见=\(window.isVisible) 位置=\(Int(window.frame.origin.x)),\(Int(window.frame.origin.y))")
            }
        }
    }

    private func placeNearMouse(_ window: NSWindow) {
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
        guard let screen else { return }
        var frame = window.frame
        frame.origin.x = mouse.x - min(180, frame.width / 3)
        frame.origin.y = mouse.y - frame.height + 36
        frame = fitted(frame, on: screen)
        window.setFrame(frame, display: false)
        Log.info("快捷启动出现在鼠标附近 \(Int(frame.origin.x)),\(Int(frame.origin.y))")
    }

    private func applySavedPanelFrame(_ window: NSWindow) {
        let settings = session.settings
        var frame = NSRect(x: settings.panelX, y: settings.panelY, width: settings.panelWidth, height: settings.panelHeight)
        frame.size.width = max(window.minSize.width, frame.width)
        frame.size.height = max(window.minSize.height, frame.height)
        let screen = NSScreen.screens.first { $0.frame.intersects(frame) } ?? NSScreen.main
        guard let screen else { return }
        frame = fitted(frame, on: screen)
        window.setFrame(frame, display: false)
    }

    private func rememberPanelFrame(_ window: NSWindow?) {
        guard let window else { return }
        let frame = window.frame
        session.settings.panelPlacedByUser = true
        session.settings.panelX = frame.origin.x
        session.settings.panelY = frame.origin.y
        session.settings.panelWidth = frame.size.width
        session.settings.panelHeight = frame.size.height
        session.save()
        Log.info("记住快捷启动位置 \(Int(frame.origin.x)),\(Int(frame.origin.y)) 尺寸 \(Int(frame.width))x\(Int(frame.height))")
    }

    private func fitted(_ frame: NSRect, on screen: NSScreen) -> NSRect {
        var frame = frame
        let area = screen.visibleFrame.insetBy(dx: 16, dy: 16)
        if frame.width > area.width { frame.size.width = area.width }
        if frame.height > area.height { frame.size.height = area.height }
        frame.origin.x = min(max(frame.origin.x, area.minX), area.maxX - frame.width)
        frame.origin.y = min(max(frame.origin.y, area.minY), area.maxY - frame.height)
        return frame
    }

    private func place(_ window: NSWindow, topRatio: CGFloat) {
        let mouse = NSEvent.mouseLocation
        let screen: NSScreen?
        switch session.settings.multiScreen {
        case "main":
            screen = NSScreen.main
        case "last":
            screen = NSScreen.screens.first { $0.localizedName == session.settings.lastScreen } ?? NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) }
        default:
            screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) }
        }
        guard let screen else { return }
        if session.settings.lastScreen != screen.localizedName {
            session.settings.lastScreen = screen.localizedName
        }
        var frame = window.frame
        let top = screen.frame.maxY - screen.frame.height * topRatio
        frame.origin.x = screen.visibleFrame.midX - frame.width / 2
        frame.origin.y = top - frame.height
        let inset: CGFloat = 16
        let area = screen.visibleFrame
        frame.origin.x = min(max(frame.origin.x, area.minX + inset), area.maxX - frame.width - inset)
        frame.origin.y = min(max(frame.origin.y, area.minY + inset), area.maxY - frame.height - inset)
        window.setFrame(frame, display: true)
    }

    private func snap(_ name: String, _ window: NSWindow, to dir: URL) {
        window.orderFront(nil)
        window.displayIfNeeded()
        guard let view = window.contentView else { return }
        let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds)
        guard let rep else { return }
        view.cacheDisplay(in: view.bounds, to: rep)
        if let data = rep.representation(using: .png, properties: [:]) {
            try? data.write(to: dir.appendingPathComponent("\(name).png"))
            Log.info("截图 \(name) \(Int(view.bounds.width))x\(Int(view.bounds.height))")
        }
    }
}

private extension NSView {
    func find(id: String) -> NSView? {
        if identifier?.rawValue == id { return self }
        for child in subviews {
            if let found = child.find(id: id) { return found }
        }
        return nil
    }
}
