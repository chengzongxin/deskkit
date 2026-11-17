import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

final class HotkeyMonitor {
    var onFire: () -> Void = {}
    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    private var keyCode: Int64 = 49
    private var needAlt = true
    private var needCommand = false
    private var needControl = false
    private var needShift = false

    func update(keyCode: Int, alt: Bool, command: Bool, control: Bool, shift: Bool) {
        self.keyCode = Int64(keyCode)
        needAlt = alt
        needCommand = command
        needControl = control
        needShift = shift
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
        Log.info("已在\(whereText)层拦截呼出键，uTools 不会再收到这个组合")
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
        let flags = event.flags
        let alt = flags.contains(.maskAlternate)
        let command = flags.contains(.maskCommand)
        let control = flags.contains(.maskControl)
        let shift = flags.contains(.maskShift)
        guard code == keyCode, alt == needAlt, command == needCommand, control == needControl, shift == needShift else {
            return Unmanaged.passUnretained(event)
        }
        // 松开和按住连发也吃掉，避免抬起时再触发一次把刚打开的浮层关掉。
        let repeating = event.getIntegerValueField(.keyboardEventAutorepeat) != 0
        guard type == .keyDown, !repeating else {
            Log.info("忽略呼出键抬起或连发")
            return nil
        }
        DispatchQueue.main.async { [weak self] in
            Log.info("收到呼出热键")
            self?.onFire()
        }
        return nil
    }
}
