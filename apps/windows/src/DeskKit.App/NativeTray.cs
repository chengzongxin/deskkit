using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DeskKit.Platform;

namespace DeskKit.App;

/// <summary>Win32 托盘，不依赖 WinForms <c>NotifyIcon</c> 在纯 WPF 下的注册行为。</summary>
sealed class NativeTray : IDisposable
{
    const int NimAdd = 0x00000000;
    const int NimDelete = 0x00000002;
    const int NimSetVersion = 0x00000004;
    const int NotifyIconVersion4 = 4;
    const uint NimfMessage = 0x00000001;
    const uint NimfIcon = 0x00000002;
    const uint NimfTip = 0x00000004;
    const uint NimfGuid = 0x00000020;
    const uint NimfShowTip = 0x00000080;
    static readonly Guid TrayGuid = new("8f4e2a1c-9d7b-4e6f-a3c5-0d6581e4a2f1");
    const uint WmContextMenu = 0x007B;
    const uint WmLButtonUp = 0x0202;
    const uint WmLButtonDown = 0x0201;
    const uint WmRButtonUp = 0x0205;
    const uint WmRButtonDown = 0x0204;
    const int CallbackMessage = 0x8000 + 88;

    readonly uint id = 3;
    HwndSource? source;
    Icon? icon;
    long lastMenuTick;

    public event Action? LeftClick;
    public event Action<int, int>? RightClick;

    public bool TryCreate(Icon trayIcon, string tip)
    {
        icon = trayIcon;
        EnsureSource();
        var data = CreateData(tip, icon.Handle);
        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            Log.Warn("Shell_NotifyIcon 添加失败");
            return false;
        }

        data.uVersionOrTimeout = NotifyIconVersion4;
        Shell_NotifyIcon(NimSetVersion, ref data);
        Log.Info("Shell 托盘已注册");
        return true;
    }

    public void Dispose()
    {
        if (source != null)
        {
            var data = CreateData("", IntPtr.Zero);
            Shell_NotifyIcon(NimDelete, ref data);
            source.RemoveHook(WndProc);
            source.Dispose();
            source = null;
        }

        icon?.Dispose();
        icon = null;
    }

    void EnsureSource()
    {
        if (source != null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("DeskKitTray")
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
        if (msg != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        var button = (uint)(lParam.ToInt64() & 0xFFFF);
        if (button == WmLButtonUp)
        {
            handled = true;
            LeftClick?.Invoke();
        }
        else if (button is WmRButtonUp or WmRButtonDown or WmContextMenu)
        {
            var now = Environment.TickCount64;
            if (now - lastMenuTick < 250)
            {
                return IntPtr.Zero;
            }

            lastMenuTick = now;
            handled = true;
            var pt = System.Windows.Forms.Cursor.Position;
            RightClick?.Invoke(pt.X, pt.Y);
        }
        else if (button == WmLButtonDown)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    NOTIFYICONDATA CreateData(string tip, IntPtr hIcon)
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = source!.Handle,
            uID = id,
            uFlags = NimfMessage | NimfIcon | NimfTip | NimfGuid | NimfShowTip,
            uCallbackMessage = CallbackMessage,
            hIcon = hIcon,
            szTip = tip.Length > 127 ? tip[..127] : tip,
            guidItem = TrayGuid
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
}