using System.Windows;
using System.Windows.Threading;
using DeskKit.Core;
using DeskKit.Platform;

namespace DeskKit.App;

public partial class App : Application
{
    Session? session;
    Shell? shell;
    HotkeyHost? hotkey;
    NativeTray? tray;
    System.Windows.Forms.NotifyIcon? legacyTray;
    System.Drawing.Icon? trayIcon;
    System.Windows.Forms.Form? traySink;
    System.Windows.Forms.ToolStripMenuItem? groupsItem;
    System.Windows.Forms.ContextMenuStrip? trayMenu;
    uint clipboardSeq;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Warn(args.Exception.ToString());
            args.Handled = true;
        };

        var root = ContractStore.Locate();
        if (root == null)
        {
            System.Windows.MessageBox.Show("找不到 contracts 目录，DeskKit 无法启动。", "DeskKit");
            Shutdown();
            return;
        }

        session = new Session(new ContractStore(root));
        session.Index = Catalog.ToolHits();
        shell = new Shell(session);
        hotkey = new HotkeyHost();
        hotkey.Fired += () => Dispatcher.Invoke(() =>
        {
            NativeMethods.ReleaseAlt();
            session.Summon();
        });
        hotkey.PanelFired += () => Dispatcher.Invoke(() =>
        {
            NativeMethods.ReleaseAlt();
            shell?.ToggleGroups();
        });
        shell.HotkeyChanged += ApplyHotkey;
        ApplyHotkey();
        Dispatcher.BeginInvoke(BuildTray, DispatcherPriority.ApplicationIdle);

        Task.Run(Indexer.Collect).ContinueWith(task =>
        {
            if (task.Exception != null)
            {
                Log.Warn($"索引失败 {task.Exception.GetBaseException().Message}");
                return;
            }

            Dispatcher.Invoke(() =>
            {
                session.Index = Catalog.ToolHits().Concat(task.Result.Select(entry => new Hit
                {
                    Id = entry.Id,
                    Kind = entry.Kind,
                    Title = entry.Title,
                    Subtitle = entry.Subtitle,
                    Path = entry.Path,
                    Keywords = entry.Keywords.ToList(),
                    Score = entry.Score
                })).ToList();
                session.OnChange();
            });
        });

        clipboardSeq = NativeMethods.GetClipboardSequenceNumber();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) => ReadClipboard();
        timer.Start();

        if (e.Args.Contains("--verify"))
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "deskkit-verify");
                shell.CaptureAll(dir);
                Console.WriteLine("VERIFY_DIR=" + dir);
            }
            finally
            {
                Shutdown();
            }

            return;
        }

        if (!session.Settings.GuideDone || e.Args.Contains("--show"))
        {
            shell.ShowGuide();
        }

        if (e.Args.Contains("--show"))
        {
            session.Summon();
        }

        Log.Info("DeskKit 已启动");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        tray = null;
        if (legacyTray != null)
        {
            legacyTray.Visible = false;
            legacyTray.Dispose();
            legacyTray = null;
        }
        trayIcon?.Dispose();
        trayIcon = null;

        if (traySink != null)
        {
            traySink.Close();
            traySink.Dispose();
            traySink = null;
        }

        hotkey?.Dispose();
        base.OnExit(e);
    }

    void ApplyHotkey()
    {
        if (session == null || shell == null || hotkey == null)
        {
            return;
        }

        var settings = session.Settings;
        var recorded = settings.Hotkeys.ContainsKey("summon");
        var vk = HotkeyMap.ToVirtualKey(settings.SummonKeyCode, recorded);
        var ok = hotkey.TryRegister(settings.SummonAlt, settings.SummonControl, settings.SummonShift, settings.SummonCommand, vk);
        var panel = settings.Hotkeys.TryGetValue("panel", out var panelBinding) ? panelBinding : null;
        var panelRecorded = panel != null;
        var panelVk = HotkeyMap.ToVirtualKey(panel?.KeyCode ?? 49, panelRecorded);
        var panelOk = hotkey.TryRegisterPanel(panel?.Alt ?? true, panel?.Control ?? false, panel?.Shift ?? true, panel?.Command ?? false, panelVk);
        shell.NoteHotkey(ok);
        if (groupsItem != null)
        {
            groupsItem.Text = "快捷启动  " + shell.PanelHotkeyText();
        }

        Log.Info(ok ? "呼出键已按设置重新注册" : "呼出键注册失败");
        Log.Info(panelOk ? $"快捷启动热键已注册 {shell.PanelHotkeyText()}" : "快捷启动热键注册失败");
    }

    void ReadClipboard()
    {
        var next = NativeMethods.GetClipboardSequenceNumber();
        if (next == clipboardSeq || session == null)
        {
            return;
        }

        clipboardSeq = next;
        try
        {
            var text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
            var files = System.Windows.Clipboard.ContainsFileDropList()
                ? System.Windows.Clipboard.GetFileDropList().Cast<string>().ToArray()
                : [];
            session.NoteClipboard(text, files);
        }
        catch (Exception ex)
        {
            Log.Warn($"读取剪贴板失败 {ex.Message}");
        }
    }

    void EnsureTraySink()
    {
        if (traySink != null)
        {
            return;
        }

        traySink = new System.Windows.Forms.Form
        {
            Text = "DeskKit",
            ShowInTaskbar = false,
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
            Width = 0,
            Height = 0,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(-20000, -20000),
            Opacity = 0
        };
        traySink.Shown += (_, _) => traySink.Hide();
        traySink.Show();
        traySink.Hide();
    }

    void BuildTray()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        EnsureTraySink();
        trayMenu = new System.Windows.Forms.ContextMenuStrip();
        var menu = trayMenu;
        menu.Items.Add("呼出", null, (_, _) => Dispatcher.Invoke(() => session?.Summon()));
        groupsItem = new System.Windows.Forms.ToolStripMenuItem("快捷启动  " + (shell?.PanelHotkeyText() ?? ""));
        groupsItem.Click += (_, _) => Dispatcher.Invoke(() => shell?.ShowGroups());
        menu.Items.Add(groupsItem);
        menu.Items.Add("常用工具", null, (_, _) => Dispatcher.Invoke(() => shell?.ShowTools()));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(() => shell?.ShowSettings()));
        menu.Items.Add("重新注册呼出键", null, (_, _) => Dispatcher.Invoke(ApplyHotkey));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Quit));
        trayIcon = AppIcon.AllocateTrayIcon();
        tray = new NativeTray();
        tray.LeftClick += () => Dispatcher.Invoke(() => session?.Summon());
        tray.RightClick += (_, _) => Dispatcher.BeginInvoke(ShowTrayMenu, DispatcherPriority.Normal);
        if (!tray.TryCreate(trayIcon, "DeskKit"))
        {
            Log.Warn("回退 WinForms NotifyIcon");
            BuildTrayWinForms(menu);
        }
    }

    void ShowTrayMenu()
    {
        if (trayMenu == null)
        {
            return;
        }

        trayMenu.Show(System.Windows.Forms.Cursor.Position);
    }

    void BuildTrayWinForms(System.Windows.Forms.ContextMenuStrip menu, bool visible = true)
    {
        legacyTray?.Dispose();
        legacyTray = new System.Windows.Forms.NotifyIcon
        {
            Icon = trayIcon ?? AppIcon.AllocateTrayIcon(),
            Text = "DeskKit",
            Visible = visible,
            ContextMenuStrip = menu
        };
        legacyTray.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(() => session?.Summon());
            }
        };
    }

    void Quit()
    {
        var answer = System.Windows.MessageBox.Show("退出后热键和剪贴板记录都会停掉。", "退出 DeskKit", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        Log.Info("退出");
        Shutdown();
    }
}
