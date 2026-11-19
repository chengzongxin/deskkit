import AppKit
import ApplicationServices
import DeskKitCore

final class AppDelegate: NSObject, NSApplicationDelegate {
    let session = Session()
    let hotkey = HotkeyMonitor()
    lazy var shell = Shell(session: session)
    var statusItem: NSStatusItem?
    var pasteCount = NSPasteboard.general.changeCount

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.appearance = nil
        NSApp.setActivationPolicy(.accessory)
        buildStatusItem()
        wire()
        shell.applyAppearance()
        shell.onHotkeyChanged = { [weak self] in self?.applyHotkey() }
        applyHotkey()
        shell.onAskHotkeyAccess = { [weak self] in self?.hotkey.promptAccessibility() }
        shell.noteHotkey(hotkey.isRunning)
        Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] _ in
            guard let self, !self.hotkey.isRunning, AXIsProcessTrusted() else { return }
            Log.info("检测到辅助功能已打开，重新抢注呼出键")
            self.hotkey.start()
            self.shell.noteHotkey(self.hotkey.isRunning)
        }
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let hits = Indexer.collect()
            DispatchQueue.main.async {
                self?.session.index = hits
                if self?.shell != nil { self?.session.onChange() }
            }
        }
        Timer.scheduledTimer(withTimeInterval: 0.8, repeats: true) { [weak self] _ in
            guard let self else { return }
            let count = NSPasteboard.general.changeCount
            if count != self.pasteCount {
                self.pasteCount = count
                self.session.noteClipboard()
            }
        }

        let args = CommandLine.arguments
        if args.contains("--verify") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { [weak self] in
                guard let self else { return }
                let dir = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("deskkit-verify")
                self.shell.captureAll(to: dir)
                print("VERIFY_DIR=\(dir.path)")
                Log.info("验证流程完成，准备退出")
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
                    NSApp.terminate(nil)
                }
            }
        } else {
            if !session.settings.guideDone || args.contains("--show") {
                shell.showGuide()
            }
            if args.contains("--show") {
                session.summon()
            }
        }
        Log.info("DeskKit 已启动")
    }

    private var groupsMenuItem: NSMenuItem?

    private func applyHotkey() {
        let settings = session.settings
        let panel = settings.hotkeys["panel"]
        hotkey.setChords([
            HotkeyChord(
                name: "summon",
                keyCode: Int64(settings.summonKeyCode),
                alt: settings.summonAlt,
                command: settings.summonCommand,
                control: settings.summonControl,
                shift: settings.summonShift,
                onFire: { [weak self] in self?.session.summon() }
            ),
            HotkeyChord(
                name: "panel",
                keyCode: Int64(panel?.keyCode ?? 2),
                alt: panel?.alt ?? true,
                command: panel?.command ?? false,
                control: panel?.control ?? false,
                shift: panel?.shift ?? false,
                onFire: { [weak self] in self?.shell.toggleGroups() }
            )
        ])
        hotkey.start()
        shell.noteHotkey(hotkey.isRunning)
        groupsMenuItem?.title = "快捷启动  \(shell.panelHotkeyText())"
        Log.info("呼出键已按设置重新注册，快捷启动热键 \(shell.panelHotkeyText())")
    }

    private func wire() {
        session.onShow = { [weak self] in self?.shell.showLauncher() }
        session.onHide = { [weak self] in self?.shell.hideLauncher() }
        session.onOpenTool = { [weak self] id in
            if id == "grid" { self?.shell.showTools() }
            else { self?.shell.showTool(id) }
        }
        session.onChange = { [weak self] in
            guard let self, self.session.machine.state != "HIDDEN" else { return }
            self.shell.rebuildLauncher()
        }
    }

    private func buildStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let url = Bundle.main.url(forResource: "AppIcon", withExtension: "icns"),
           let image = NSImage(contentsOf: url) {
            image.size = NSSize(width: 18, height: 18)
            item.button?.image = image
        } else {
            item.button?.title = "D"
        }
        let menu = NSMenu()
        menu.addItem(menuItem("呼出", #selector(summon)))
        let groups = menuItem("快捷启动", #selector(showGroups))
        groupsMenuItem = groups
        menu.addItem(groups)
        menu.addItem(menuItem("常用工具", #selector(showTools)))
        menu.addItem(menuItem("设置", #selector(showSettings)))
        menu.addItem(menuItem("重新注册呼出键", #selector(reregisterHotkey)))
        menu.addItem(menuItem("打开辅助功能设置", #selector(openAccessibility)))
        menu.addItem(.separator())
        menu.addItem(menuItem("退出", #selector(quit)))
        item.menu = menu
        statusItem = item
    }

    private func menuItem(_ title: String, _ action: Selector) -> NSMenuItem {
        let entry = NSMenuItem(title: title, action: action, keyEquivalent: "")
        entry.target = self
        return entry
    }

    @objc private func summon() { session.summon() }
    @objc private func showGroups() { shell.showGroups() }
    @objc private func showTools() { shell.showTools() }
    @objc private func showSettings() { shell.showSettings() }
    @objc private func reregisterHotkey() {
        Log.info("手动重新注册呼出键")
        hotkey.start()
        shell.noteHotkey(hotkey.isRunning)
        if !hotkey.isRunning { hotkey.promptAccessibility() }
    }

    @objc private func openAccessibility() { hotkey.promptAccessibility() }
    @objc private func quit() {
        Log.info("退出")
        NSApp.terminate(nil)
    }
}

let appDelegate = AppDelegate()
let application = NSApplication.shared
application.appearance = nil
application.delegate = appDelegate
application.run()
