import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

struct HotkeyChord {
    var name: String
    var keyCode: Int64
    var alt: Bool
    var command: Bool
    var control: Bool
    var shift: Bool
    var onFire: () -> Void
    var held = false

    func matches(code: Int64, alt: Bool, command: Bool, control: Bool, shift: Bool) -> Bool {
        self.keyCode == code && self.alt == alt && self.command == command && self.control == control && self.shift == shift
    }
}

final class HotkeyMonitor {
    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    private var chords: [HotkeyChord] = []

    func setChords(_ chords: [HotkeyChord]) {
        self.chords = chords
    }

    var isRunning: Bool { tap != nil }

    func start() {
        stop()
        let trusted = AXIsProcessTrusted()
        if !trusted {
            Log.warn("辅助功能未授权，还抢不到 Option+Space。请允许 /Applications/DeskKit.app")
            return
        }
        let mask = CGEventMask((1 << CGEventType.keyDown.rawValue) | (1 << CGEventType.keyUp.rawValue))
        let refcon = Unmanaged.passUnretained(self).toOpaque()
        let callback: CGEventTapCallBack = { _, type, event, user in
            guard let user else { return Unmanaged.passUnretained(event) }
            let monitor = Unmanaged<HotkeyMonitor>.fromOpaque(user).takeUnretainedValue()
            return monitor.handle(type: type, event: event)
        }
        // 插在事件链最前面，先于 uTools 吃掉这个组合键。
        let locations: [(CGEventTapLocation, String)] = [(.cghidEventTap, "HID"), (.cgSessionEventTap, "会话")]
        var created: CFMachPort?
        var whereText = ""
        for item in locations {
            created = CGEvent.tapCreate(tap: item.0, place: .headInsertEventTap, options: .defaultTap, eventsOfInterest: mask, callback: callback, userInfo: refcon)
            if created != nil {
                whereText = item.1
                break
            }
        }
        guard let tap = created else {
            Log.warn("呼出热键注册失败。辅助功能已开的话，退出 DeskKit 再打开一次")
            return
        }
        self.tap = tap
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        self.source = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        Log.info("已在\(whereText)层拦截全局热键")
    }

    func stop() {
        if let tap {
            CGEvent.tapEnable(tap: tap, enable: false)
        }
        if let source {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes)
        }
        tap = nil
        source = nil
    }

    func promptAccessibility() {
        if AXIsProcessTrusted() {
            Log.info("辅助功能已经授予，直接注册呼出键")
            start()
            return
        }
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        let options = [key: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        let urls = [
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
            "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_Accessibility"
        ]
        for text in urls {
            if let url = URL(string: text), NSWorkspace.shared.open(url) {
                break
            }
        }
        Log.info("已打开辅助功能设置。如果 DeskKit 的开关已经是打开的，先关掉再打开一次")
    }

    private func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return Unmanaged.passUnretained(event)
        }
        let code = event.getIntegerValueField(.keyboardEventKeycode)
        // 抬起只按键码认。按住 Option 连按 Space 和 D 时，抬起往往已经不带 Option。
        // 若还要求修饰键一致，held 会一直为真，之后的热键全被吃掉，窗口就关不掉。
        if type == .keyUp {
            var swallow = false
            for index in chords.indices where chords[index].keyCode == code && chords[index].held {
                chords[index].held = false
                swallow = true
            }
            return swallow ? nil : Unmanaged.passUnretained(event)
        }
        guard type == .keyDown else {
            return Unmanaged.passUnretained(event)
        }
        let flags = event.flags
        let alt = flags.contains(.maskAlternate)
        let command = flags.contains(.maskCommand)
        let control = flags.contains(.maskControl)
        let shift = flags.contains(.maskShift)
        guard let index = chords.firstIndex(where: { $0.matches(code: code, alt: alt, command: command, control: control, shift: shift) }) else {
            return Unmanaged.passUnretained(event)
        }
        // 连发、以及同一次按下里的第二条 keyDown 都吃掉，避免刚打开又被关掉。
        let repeating = event.getIntegerValueField(.keyboardEventAutorepeat) != 0
        if repeating || chords[index].held {
            return nil
        }
        chords[index].held = true
        let name = chords[index].name
        let fire = chords[index].onFire
        DispatchQueue.main.async {
            Log.info("收到热键 \(name)")
            fire()
        }
        return nil
    }
}
