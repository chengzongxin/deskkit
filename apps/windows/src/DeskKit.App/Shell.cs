using System.Windows;
using System.Windows.Media.Imaging;
using DeskKit.Platform;

namespace DeskKit.App;

public sealed class Shell
{
    public Session Session { get; }
    public Chrome Chrome { get; }
    public LauncherWindow Launcher { get; }
    public GroupsWindow Groups { get; }
    public ToolsWindow Tools { get; }
    public SettingsWindow Settings { get; }
    public GuideWindow Guide { get; }
    public bool HotkeyReady { get; private set; }
    public bool SuppressBlur { get; set; }
    public string? RecordingHotkey { get; set; }
    public event Action? HotkeyChanged;

    public Shell(Session session)
    {
        Session = session;
        Chrome = new Chrome(session);
        Launcher = new LauncherWindow(this);
        Groups = new GroupsWindow(this);
        Tools = new ToolsWindow(this);
        Settings = new SettingsWindow(this);
        Guide = new GuideWindow(this);
        session.OnShow = ShowLauncher;
        session.OnHide = HideLauncher;
        session.OnOpenTool = id =>
        {
            if (id == "grid") ShowTools();
            else ShowTool(id);
        };
        session.OnChange = () =>
        {
            if (session.Machine.State != "HIDDEN" && Launcher.IsVisible)
            {
                Launcher.Rebuild();
            }

            if (Groups.IsVisible)
            {
                Groups.Render();
            }
        };
    }

    public void ShowLauncher()
    {
        Chrome.PresentLauncher(Launcher);
        Launcher.LockFrame();
        Launcher.Rebuild();
        Launcher.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
        {
            Launcher.LockFrame();
            Launcher.EnforceFrame();
            Launcher.FocusSearch("呼出");
        });
        Launcher.Dispatcher.BeginInvoke(() => Launcher.FocusSearch("呼出延迟"));
        Log.Info($"浮层 {Launcher.Width:0}x{Launcher.Height:0} 状态 {Session.Machine.State}");
    }

    public void HideLauncher()
    {
        Launcher.UnlockFrame();
        Launcher.Hide();
    }

    public void ShowTools() => Tools.ShowGrid();

    public void ShowTool(string id) => Tools.ShowPage(id);

    public void CloseTool() => Tools.CloseCurrent();

    public void ShowGroups()
    {
        var fresh = !Groups.IsVisible;
        if (fresh)
        {
            Groups.Width = Math.Max(760, Groups.Width);
            Groups.Height = Math.Max(480, Groups.Height);
            if (Session.Settings.PanelPlacedByUser && Session.Settings.PanelWidth >= 760 && Session.Settings.PanelHeight >= 480)
            {
                Chrome.ApplySavedFrame(Groups, Session.Settings.PanelX, Session.Settings.PanelY, Session.Settings.PanelWidth, Session.Settings.PanelHeight);
            }
            else
            {
                Chrome.PlaceNearCursor(Groups);
            }
        }

        Groups.Render();
        if (fresh)
        {
            Groups.Show();
        }

        Groups.Activate();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(Groups).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.ForceForeground(hwnd);
        }
    }

    public string PanelHotkeyText()
    {
        if (Session.Settings.Hotkeys.TryGetValue("panel", out var saved) && saved.Text.Length > 0)
        {
            return saved.Text;
        }

        return Session.Store.FormatChord("windows", "panel");
    }

    public void ToggleGroups()
    {
        if (Groups.IsVisible)
        {
            Groups.Hide();
            Log.Info("热键隐藏快捷启动");
            return;
        }

        Log.Info("热键唤出快捷启动");
        ShowGroups();
    }

    public void ShowSettings(string tab = "general") => Settings.Open(tab);

    public void ShowGuide() => Guide.Open();

    public void NoteHotkey(bool ready)
    {
        if (HotkeyReady == ready) return;
        HotkeyReady = ready;
        Log.Info(ready ? "呼出键已生效" : "呼出键未生效：组合键被占用");
        if (Launcher.IsVisible) Launcher.Rebuild();
    }

    public void RaiseHotkeyChanged() => HotkeyChanged?.Invoke();

    public void RefreshSurfaces()
    {
        Chrome.Invalidate();
        if (Launcher.IsVisible) Launcher.Rebuild();
        if (Groups.IsVisible) Groups.Render();
        if (Settings.IsVisible) Settings.Render();
        if (Tools.IsVisible) Tools.Render();
        if (Guide.IsVisible) Guide.Render();
    }

    public void CaptureAll(string directory)
    {
        Directory.CreateDirectory(directory);
        Session.Query = "";
        Session.TriggerWord = "";
        Session.Hits = [];
        Session.Machine.Transition("HIDDEN");
        Session.Machine.Transition("A1");
        ShowLauncher();
        Snap("A1-home", Launcher, directory);
        Session.Query = "wx";
        Session.Search();
        Launcher.Rebuild();
        Snap("A3-results", Launcher, directory);
        Session.Query = "zzzznope";
        Session.Search();
        Launcher.Rebuild();
        Snap("A4-empty", Launcher, directory);
        Session.Query = "fy hello";
        Session.Search();
        Launcher.Rebuild();
        Snap("A-trigger", Launcher, directory);
        ShowGroups();
        Snap("B-panel", Groups, directory);
        ShowTools();
        Snap("C-grid", Tools, directory);
        ShowTool("calc");
        Snap("C-calc", Tools, directory);
        ShowSettings("general");
        Snap("E-general", Settings, directory);
        ShowSettings("hotkeys");
        Snap("E-hotkeys", Settings, directory);
        ShowSettings("about");
        Snap("E-about", Settings, directory);
        Guide.Open();
        Snap("F-step1", Guide, directory);
        Log.Info($"验证截图已写入 {directory}");
    }

    static void Snap(string name, Window window, string directory)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)window.ActualWidth);
        var height = Math.Max(1, (int)window.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
        Log.Info($"截图 {name} {width}x{height}");
    }
}
