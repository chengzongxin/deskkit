using System.Windows.Interop;

namespace DeskKit.Platform;

/// <summary>
/// 在隐藏消息窗口上注册全局热键。成功后这个组合不再打开系统菜单。
/// </summary>
public sealed class HotkeyHost : IDisposable
{
    const int HotkeyId = 1;
    HwndSource? source;
    bool registered;

    public event Action? Fired;

    public bool TryRegister(bool alt, bool control, bool shift, bool win, int virtualKey)
    {
        Ensure();
        Unregister();
        uint modifiers = NativeMethods.ModNoRepeat;
        if (alt) modifiers |= NativeMethods.ModAlt;
        if (control) modifiers |= NativeMethods.ModControl;
        if (shift) modifiers |= NativeMethods.ModShift;
        if (win) modifiers |= NativeMethods.ModWin;
        registered = NativeMethods.RegisterHotKey(source!.Handle, HotkeyId, modifiers, (uint)virtualKey);
        if (!registered)
        {
            Log.Warn("呼出热键注册失败，组合键可能已被占用");
        }

        return registered;
    }

    public void Unregister()
    {
        if (source != null && registered)
        {
            NativeMethods.UnregisterHotKey(source.Handle, HotkeyId);
        }

        registered = false;
    }

    public void Dispose()
    {
        Unregister();
        source?.Dispose();
        source = null;
    }

    void Ensure()
    {
        if (source != null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("DeskKitHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3)
        };
        source = new HwndSource(parameters);
        source.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Log.Info("收到呼出热键");
            Fired?.Invoke();
        }

        return IntPtr.Zero;
    }
}

/// <summary>macOS 默认把空格记成键码 49。Windows 录制之后存的是虚拟键码。</summary>
public static class HotkeyMap
{
    public static int ToVirtualKey(int keyCode, bool recordedOnWindows)
    {
        if (recordedOnWindows)
        {
            return keyCode;
        }

        return keyCode switch
        {
            49 => 0x20,
            36 => 0x0D,
            48 => 0x09,
            53 => 0x1B,
            43 => 0xBC,
            _ => keyCode
        };
    }
}
