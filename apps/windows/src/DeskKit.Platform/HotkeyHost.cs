using System.Windows.Interop;

namespace DeskKit.Platform;

/// <summary>
/// 在隐藏消息窗口上注册全局热键。成功后这个组合不再打开系统菜单。
/// </summary>
public sealed class HotkeyHost : IDisposable
{
    const int SummonId = 1;
    const int PanelId = 2;
    HwndSource? source;
    bool summonRegistered;
    bool panelRegistered;

    public event Action? Fired;
    public event Action? PanelFired;

    public bool TryRegister(bool alt, bool control, bool shift, bool win, int virtualKey)
    {
        return Register(SummonId, alt, control, shift, win, virtualKey, "呼出热键注册失败，组合键可能已被占用");
    }

    public bool TryRegisterPanel(bool alt, bool control, bool shift, bool win, int virtualKey)
    {
        return Register(PanelId, alt, control, shift, win, virtualKey, "快捷启动热键注册失败，组合键可能已被占用");
    }

    bool Register(int id, bool alt, bool control, bool shift, bool win, int virtualKey, string failure)
    {
        Ensure();
        NativeMethods.UnregisterHotKey(source!.Handle, id);
        uint modifiers = NativeMethods.ModNoRepeat;
        if (alt) modifiers |= NativeMethods.ModAlt;
        if (control) modifiers |= NativeMethods.ModControl;
        if (shift) modifiers |= NativeMethods.ModShift;
        if (win) modifiers |= NativeMethods.ModWin;
        var ok = NativeMethods.RegisterHotKey(source.Handle, id, modifiers, (uint)virtualKey);
        if (id == SummonId) summonRegistered = ok;
        if (id == PanelId) panelRegistered = ok;
        if (!ok) Log.Warn(failure);
        return ok;
    }

    public void Unregister()
    {
        if (source != null)
        {
            if (summonRegistered) NativeMethods.UnregisterHotKey(source.Handle, SummonId);
            if (panelRegistered) NativeMethods.UnregisterHotKey(source.Handle, PanelId);
        }

        summonRegistered = false;
        panelRegistered = false;
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
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == SummonId)
        {
            handled = true;
            Log.Info("收到呼出热键");
            Fired?.Invoke();
        }

        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == PanelId)
        {
            handled = true;
            Log.Info("收到快捷启动热键");
            PanelFired?.Invoke();
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
            2 => 0x44,
            49 => 0x20,
            36 => 0x0D,
            48 => 0x09,
            53 => 0x1B,
            43 => 0xBC,
            _ => keyCode
        };
    }
}
