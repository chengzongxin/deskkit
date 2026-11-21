using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DeskKit.Platform;

public static class Log
{
    static readonly string FilePath = Path.Combine(Paths.Root, "logs", "deskkit.log");
    static readonly ConcurrentQueue<string> Pending = new();
    static int pumping;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    static void Write(string level, string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {level} {message}";
        Console.Error.WriteLine(line);
        Pending.Enqueue(line);
        if (Interlocked.CompareExchange(ref pumping, 1, 0) == 0)
        {
            Task.Run(Flush);
        }
    }

    static void Flush()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            while (Pending.TryDequeue(out var line))
            {
                writer.WriteLine(line);
            }
        }
        catch
        {
            // 日志写失败不能影响主流程。
        }
        finally
        {
            Interlocked.Exchange(ref pumping, 0);
            if (!Pending.IsEmpty && Interlocked.CompareExchange(ref pumping, 1, 0) == 0)
            {
                Task.Run(Flush);
            }
        }
    }
}

public static class Paths
{
    public static string Root { get; } = Create();

    public static string File(string name) => Path.Combine(Root, name);

    static string Create()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskKit");
        Directory.CreateDirectory(root);
        return root;
    }
}

public static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const int GwlExStyle = -20;
    public const int WsExToolWindow = 0x00000080;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;
    const byte VkMenu = 0x12;
    const uint KeyeventfKeyup = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    const int VkLButton = 0x01;

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    /// <summary>左键是否还按着。失焦时用来区分「点一下外面」和「按住准备拖文件」。</summary>
    public static bool IsLeftButtonDown() => (GetAsyncKeyState(VkLButton) & 0x8000) != 0;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;

    public static void MakeToolWindow(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExToolWindow);
    }

    public static void ForceForeground(IntPtr hwnd)
    {
        var foreground = GetForegroundWindow();
        var foreThread = GetWindowThreadProcessId(foreground, out _);
        var appThread = GetCurrentThreadId();
        if (foreThread != 0 && foreThread != appThread)
        {
            AttachThreadInput(foreThread, appThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(foreThread, appThread, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }
    }

    /// <summary>热键里的 Alt 松开后要补一次抬起，否则接下来打字会带着 Alt。</summary>
    public static void ReleaseAlt() => keybd_event(VkMenu, 0, KeyeventfKeyup, UIntPtr.Zero);
}

public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue("DeskKit", throwOnMissingValue: false);
            return;
        }

        var exe = Environment.ProcessPath ?? "";
        key.SetValue("DeskKit", "\"" + exe + "\"");
    }
}
