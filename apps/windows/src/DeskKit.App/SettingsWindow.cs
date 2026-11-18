using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeskKit.Platform;
using Microsoft.Win32;

namespace DeskKit.App;

public sealed class SettingsWindow : Window
{
    readonly Shell shell;
    string tab = "general";
    bool rendering;
    Chrome C => shell.Chrome;
    Session S => shell.Session;

    public SettingsWindow(Shell owner)
    {
        shell = owner;
        Title = "设置";
        Width = S.Store.Layout("windows", "settings", "width");
        Height = S.Store.Layout("windows", "settings", "height");
        C.Prepare(this);
        PreviewKeyDown += OnKey;
    }

    public void Open(string next)
    {
        tab = next;
        Render();
        C.Present(this, 0.12);
        Log.Info($"打开设置 {tab}");
    }

    public void Render()
    {
        rendering = true;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        var header = new DockPanel { Margin = new Thickness(16, 0, 8, 0), Background = C.Brush("bg.float") };
        var close = C.IconButton(Glyph.Close, Hide);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(C.Label("设置", 16, FontWeights.SemiBold, "text.primary"));
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S.Store.Layout("windows", "navWidth")) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nav = new StackPanel { Margin = new Thickness(8, 12, 8, 0) };
        nav.Children.Add(C.Label("设置", 11, FontWeights.SemiBold, "text.tertiary"));
        foreach (var item in new[] { ("general", "常规"), ("hotkeys", "快捷键"), ("about", "关于与双端信息") })
        {
            var id = item.Item1;
            var on = tab == id;
            var row = new Border
            {
                Height = 34,
                Margin = new Thickness(0, 6, 0, 0),
                CornerRadius = new CornerRadius(8),
                Background = on ? C.Brush("accent.soft") : System.Windows.Media.Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = C.Label(item.Item2, 13, on ? FontWeights.SemiBold : FontWeights.Normal, on ? "accent.hover" : "text.secondary")
            };
            ((TextBlock)row.Child).Margin = new Thickness(12, 0, 0, 0);
            row.MouseLeftButtonUp += (_, _) => { tab = id; Render(); };
            nav.Children.Add(row);
        }

        Grid.SetColumn(nav, 0);
        body.Children.Add(nav);
        var page = new StackPanel();
        if (tab == "general") General(page);
        else if (tab == "hotkeys") Hotkeys(page);
        else About(page);
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var foot = C.Label("所有设置自动保存 · 快捷键与设置结构在 macOS 与 Windows 使用同一套契约", 12, FontWeights.Normal, "text.tertiary");
        foot.Margin = new Thickness(16, 0, 16, 0);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Content = C.Float(root, "bg.canvas");
        rendering = false;
    }

    void General(Panel page)
    {
        page.Children.Add(C.Card("启动与呼出",
        [
            ("开机自启", "登录后自动常驻，随时响应全局呼出键", C.Switch(S.Settings.AutoLaunch, on =>
            {
                if (rendering) return;
                S.Settings.AutoLaunch = on;
                try { AutoStart.Set(on); Log.Info($"开机自启 {on}"); }
                catch (Exception ex) { Log.Warn($"开机自启设置失败 {ex.Message}"); }
                S.Save();
            })),
            ("呼出方式", "全局快捷键、托盘，或仅从托盘打开", C.Menu([("hotkey", "全局快捷键"), ("menu", "托盘"), ("edge", "仅从托盘打开")], S.Settings.SummonMode, value => SavePick(() => S.Settings.SummonMode = value, "呼出方式"))),
            ("失焦自动隐藏", "点击浮层外 150ms 后隐藏", C.Switch(S.Settings.BlurHide, on => SavePick(() => S.Settings.BlurHide = on, "失焦自动隐藏"))),
            ("多屏行为", "浮层靠近屏幕边缘自动内收，不跨屏裁切", C.Menu([("followCursor", "跟随鼠标所在屏"), ("last", "记住上次呼出屏"), ("main", "固定主屏")], S.Settings.MultiScreen, value => SavePick(() => S.Settings.MultiScreen = value, "多屏行为")))
        ]));
        page.Children.Add(C.Card("外观",
        [
            ("主题", "浅色为默认。深色和跟随系统会立刻切换外观", C.Chips([("light", "浅色"), ("dark", "深色"), ("system", "跟随系统")], S.Settings.Theme, value => { S.Settings.Theme = value; S.Save(); shell.RefreshSurfaces(); })),
            ("强调色", "选中结果和主按钮使用这个颜色", C.Chips([("accent", "蓝"), ("success", "绿"), ("warning", "黄"), ("danger", "红")], S.Settings.AccentToken, value => { S.Settings.AccentToken = value; S.Save(); shell.RefreshSurfaces(); })),
            ("结果密度", "紧凑 40px，标准 48px", C.Chips([("compact", "紧凑"), ("standard", "标准")], S.Settings.Density, value => SavePick(() => S.Settings.Density = value, "结果密度"))),
            ("背景图片", "用于浮层、快捷启动、设置和工具页的底图", BackgroundButtons()),
            ("背景透明度", "只影响底图或底色，文字和按钮保持清晰", OpacityRow())
        ]));
        page.Children.Add(C.Card("数据与隐私",
        [
            ("本地优先", "分组、设置和剪贴板只存在本机", DataMenu()),
            ("可选云同步", "当前版本只记录开关，数据不会上传", C.Switch(S.Settings.CloudSync, on => { S.Settings.CloudSync = on; S.Save(); Log.Info(on ? "已记下云同步开关。当前版本数据仍只在本机" : "云同步保持关闭"); })),
            ("剪贴板保留时长", "超过期限的记录会在下次复制时清掉", C.Menu([("7d", "7 天"), ("30d", "30 天"), ("forever", "永久")], S.Settings.ClipboardRetention, value => { S.Settings.ClipboardRetention = value; S.Save(); S.PruneClips(); })),
            ("剪贴板记录", "暂停后不再写入，也可以马上清空", ClipButtons()),
            ("语言", "界面文案为简体中文", C.Label("简体中文", 12, FontWeights.SemiBold, "text.primary")),
            ("主程序更新", "关于页的检查更新会读取这个开关", C.Switch(S.Settings.AutoUpdate, on => SavePick(() => S.Settings.AutoUpdate = on, "主程序更新")))
        ]));
    }

    void Hotkeys(Panel page)
    {
        var specs = new[]
        {
            ("summon", "主呼出键", "全局呼出或隐藏浮层"),
            ("tools", "常用工具面板", "打开 3×3 工具宫格"),
            ("clipboard", "剪贴板历史", "直达剪贴板记录"),
            ("settings", "设置页", "打开设置")
        };
        page.Children.Add(C.Card("快捷键 · 录制后自动保存", specs.Select(item => (item.Item2, item.Item3, (UIElement?)HotkeyControls(item.Item1))).ToArray()));
        var triggers = S.Store.Triggers.Where(item => item.ToolId != "ocr" && item.Word.Length > 0).ToList();
        var rows = triggers.Select(item =>
        {
            var field = new TextBox { Text = S.Settings.TriggerWords.TryGetValue(item.ToolId, out var word) ? word : item.Word, Width = 120, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
            field.LostFocus += (_, _) => SaveTrigger(item.ToolId, field.Text);
            field.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveTrigger(item.ToolId, field.Text); };
            return (item.Label, $"输入「{field.Text} + 空格」进入{item.Label}。回车保存", (UIElement?)field);
        }).ToArray();
        page.Children.Add(C.Card("触发词", rows));
        page.Children.Add(C.Card("被系统或其他应用占用",
        [
            ("说明", "录制时需要带上 Ctrl、Alt、Shift 或 Win。如果和已有快捷键相同，这一行会标成冲突，不会悄悄覆盖。Esc 取消录制。", C.Label("", 11, FontWeights.Normal, "text.tertiary"))
        ]));
    }

    void About(Panel page)
    {
        var mb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d;
        page.Children.Add(C.Card("",
        [
            ("DeskKit 0.9.0", "Windows 版已按 macOS 同一套契约补齐界面和功能。", C.Label("当前版本", 12, FontWeights.SemiBold, "text.primary"))
        ]));
        var diffs = string.Join("\n", S.Store.StagedDiffs.Where(item => item.Owner is "windows" or "both").Select(item => $"{item.Id} · {item.DueAt} · {item.ParityPlan}"));
        page.Children.Add(C.Card("双端功能对齐",
        [
            ("阶段性差异", diffs, C.Label("来自契约", 12, FontWeights.SemiBold, "text.secondary"))
        ]));
        page.Children.Add(C.Card("更新日志",
        [
            ("0.9.0", "全局呼出、拼音搜索、内置工具、分组快捷启动、三页设置", C.Label("当前版本", 12, FontWeights.SemiBold, "text.primary"))
        ]));
        page.Children.Add(C.Card("Windows 系统能力", S.Store.Permissions("windows").Select(item =>
        {
            var granted = item.Id switch
            {
                "autostart" => S.Settings.AutoLaunch ? "已开启" : "未开启",
                "fileIndex" => S.Index.Count > 0 ? "已索引" : "索引中",
                _ => "可用"
            };
            return (item.Title, item.Reason, (UIElement?)C.Label(granted, 12, FontWeights.SemiBold, granted.StartsWith("未") ? "warning" : "success"));
        }).ToArray()));
        page.Children.Add(C.Card("数据 / 检查",
        [
            ("数据目录", "打开、导出或清空都作用在本机数据上", DataMenu()),
            ("检查更新", S.Settings.AutoUpdate ? "已开启启动检查。现在可以手动查一次" : "自动检查已关闭，仍可手动查看版本", C.Soft("检查更新", CheckUpdate)),
            ("重新走引导", "三步引导可以再看一遍", C.Soft("重新引导", () => { S.Settings.GuideDone = false; S.Save(); shell.ShowGuide(); })),
            ("实测内存", "记录当前占用，不作为发布门槛", C.Label($"{mb:0} MB", 12, FontWeights.SemiBold, "text.primary"))
        ]));
    }

    UIElement HotkeyControls(string id)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(C.Keycap(HotkeyText(id)));
        panel.Children.Add(C.Soft(shell.RecordingHotkey == id ? "请按键…" : "录制", () =>
        {
            shell.RecordingHotkey = id;
            Log.Info($"开始录制快捷键 {id}");
            Render();
            Focus();
        }));
        panel.Children.Add(C.Soft("恢复默认", () =>
        {
            S.Settings.Hotkeys.Remove(id);
            if (id == "summon")
            {
                S.Settings.SummonKeyCode = 49;
                S.Settings.SummonAlt = true;
                S.Settings.SummonCommand = false;
                S.Settings.SummonControl = false;
                S.Settings.SummonShift = false;
                shell.RaiseHotkeyChanged();
            }

            S.Save();
            Render();
        }));
        var conflict = Conflict(id);
        panel.Children.Add(C.Label(conflict ?? "可用", 11, FontWeights.SemiBold, conflict == null ? "success" : "danger"));
        return panel;
    }

    StackPanel BackgroundButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(C.Soft(S.Settings.BackgroundImagePath.Length == 0 ? "选择图片" : "更换图片", ChooseBackground));
        panel.Children.Add(C.Soft("清除", () =>
        {
            if (S.Settings.BackgroundImagePath.Length > 0 && File.Exists(S.Settings.BackgroundImagePath))
            {
                try { File.Delete(S.Settings.BackgroundImagePath); } catch { }
            }

            S.Settings.BackgroundImagePath = "";
            S.Save();
            shell.RefreshSurfaces();
        }));
        return panel;
    }

    StackPanel OpacityRow()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var slider = new Slider { Minimum = 0.15, Maximum = 1, Value = S.Settings.BackgroundOpacity, Width = 140, VerticalAlignment = VerticalAlignment.Center };
        var label = C.Label($"{(int)(S.Settings.BackgroundOpacity * 100)}%", 12, FontWeights.SemiBold, "text.secondary");
        slider.ValueChanged += (_, _) =>
        {
            if (rendering) return;
            S.Settings.BackgroundOpacity = slider.Value;
            label.Text = $"{(int)(slider.Value * 100)}%";
            S.Save();
            shell.RefreshSurfaces();
        };
        panel.Children.Add(slider);
        panel.Children.Add(label);
        return panel;
    }

    StackPanel ClipButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(C.Soft(S.Settings.ClipboardPaused ? "恢复记录" : "暂停记录", () =>
        {
            S.Settings.ClipboardPaused = !S.Settings.ClipboardPaused;
            S.Save();
            Render();
        }));
        panel.Children.Add(C.Soft("清空", () => { S.Clips.Clear(); S.Save(); Render(); }));
        return panel;
    }

    UIElement DataMenu() => C.Menu([("open", "打开数据目录"), ("export", "导出数据"), ("clear", "清空剪贴板")], "open", value =>
    {
        if (rendering) return;
        switch (value)
        {
            case "export": Export(); break;
            case "clear": S.Clips.Clear(); S.Save(); break;
            default: Session.Open(Paths.Root); break;
        }
    });

    void ChooseBackground()
    {
        shell.SuppressBlur = true;
        var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp", Title = "设为背景" };
        var ok = dialog.ShowDialog() == true;
        shell.SuppressBlur = false;
        if (!ok) return;
        var ext = System.IO.Path.GetExtension(dialog.FileName);
        if (ext.Length == 0) ext = ".png";
        var destination = Paths.File("background" + ext);
        File.Copy(dialog.FileName, destination, overwrite: true);
        S.Settings.BackgroundImagePath = destination;
        S.Save();
        shell.RefreshSurfaces();
    }

    void Export()
    {
        shell.SuppressBlur = true;
        var dialog = new OpenFolderDialog { Title = "导出到这里" };
        var ok = dialog.ShowDialog() == true;
        shell.SuppressBlur = false;
        if (!ok) return;
        var destination = System.IO.Path.Combine(dialog.FolderName, "DeskKit-export");
        Directory.CreateDirectory(destination);
        foreach (var name in new[] { "settings.json", "groups.json", "clipboard.json", "recents.json" })
        {
            var source = Paths.File(name);
            if (File.Exists(source)) File.Copy(source, System.IO.Path.Combine(destination, name), overwrite: true);
        }

        Log.Info($"已导出数据到 {destination}");
        Session.Open(destination);
    }

    void CheckUpdate()
    {
        var message = S.Settings.AutoUpdate ? "当前已是 DeskKit 0.9.0，没有新版本。" : "自动检查已关闭。当前版本仍是 DeskKit 0.9.0。";
        Log.Info(message);
        System.Windows.MessageBox.Show(message, "检查更新");
    }

    void SavePick(Action apply, string name)
    {
        if (rendering) return;
        apply();
        S.Save();
        Log.Info($"{name}已更新");
    }

    void SaveTrigger(string toolId, string text)
    {
        var word = text.Trim();
        if (toolId.Length == 0 || word.Length == 0) return;
        var clash = S.Settings.TriggerWords.Any(pair => pair.Key != toolId && string.Equals(pair.Value, word, StringComparison.OrdinalIgnoreCase))
            || S.Store.Triggers.Any(item => item.ToolId != toolId && string.Equals(item.Word, word, StringComparison.OrdinalIgnoreCase) && !S.Settings.TriggerWords.ContainsKey(item.ToolId));
        if (clash)
        {
            Log.Warn($"触发词 {word} 已被占用，没有保存");
            Render();
            return;
        }

        S.Settings.TriggerWords[toolId] = word;
        S.Save();
        Log.Info($"触发词 {toolId} 改为 {word}");
    }

    string HotkeyText(string id) => S.Settings.Hotkeys.TryGetValue(id, out var saved) ? saved.Text : S.Store.FormatChord("windows", id);

    string? Conflict(string id)
    {
        if (!S.Settings.Hotkeys.TryGetValue(id, out var mine)) return null;
        var other = S.Settings.Hotkeys.FirstOrDefault(pair => pair.Key != id && pair.Value.Text == mine.Text);
        return string.IsNullOrEmpty(other.Key) ? null : $"与{other.Key}冲突";
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (shell.RecordingHotkey == null)
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            shell.RecordingHotkey = null;
            Log.Info("取消录制快捷键");
            Render();
            e.Handled = true;
            return;
        }

        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            Log.Warn("录制快捷键需要至少一个修饰键");
            e.Handled = true;
            return;
        }

        var id = shell.RecordingHotkey;
        var binding = new HotkeyBinding
        {
            KeyCode = KeyInterop.VirtualKeyFromKey(key),
            Alt = mods.HasFlag(ModifierKeys.Alt),
            Control = mods.HasFlag(ModifierKeys.Control),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Command = mods.HasFlag(ModifierKeys.Windows),
            Text = LabelFor(mods, key)
        };
        if (S.Settings.Hotkeys.Any(pair => pair.Key != id && pair.Value.Text == binding.Text))
        {
            Log.Warn($"快捷键 {binding.Text} 冲突，没有保存");
            e.Handled = true;
            return;
        }

        S.Settings.Hotkeys[id] = binding;
        if (id == "summon")
        {
            S.Settings.SummonKeyCode = binding.KeyCode;
            S.Settings.SummonAlt = binding.Alt;
            S.Settings.SummonControl = binding.Control;
            S.Settings.SummonShift = binding.Shift;
            S.Settings.SummonCommand = binding.Command;
            shell.RaiseHotkeyChanged();
        }

        S.Save();
        shell.RecordingHotkey = null;
        Log.Info($"已录制 {id} {binding.Text}");
        Render();
        e.Handled = true;
    }

    string LabelFor(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add(S.Store.KeyLabel("windows", "ctrl"));
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add(S.Store.KeyLabel("windows", "alt"));
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add(S.Store.KeyLabel("windows", "shift"));
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add(S.Store.KeyLabel("windows", "command"));
        parts.Add(key == Key.Space ? "Space" : key == Key.OemComma ? "," : key.ToString());
        return string.Join("+", parts);
    }
}
