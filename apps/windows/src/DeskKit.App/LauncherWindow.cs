using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskKit.Platform;

namespace DeskKit.App;

public sealed class LauncherWindow : Window
{
    readonly Shell shell;
    readonly TextBox box;
    readonly TextBlock hint;
    readonly Border capsule;
    readonly TextBlock capsuleText;
    readonly Button clear;
    readonly StackPanel body;
    readonly ScrollViewer scroll;
    readonly StackPanel footerHints;
    readonly TextBlock footerAlert;
    readonly Border footerBar;
    readonly Border root;
    bool updating;
    bool footerReady;
    string scrollKey = "";
    List<Hit> homeList = [];
    NativeMethods.WinRect? lockedFrame;

    Chrome C => shell.Chrome;
    Session S => shell.Session;

    public LauncherWindow(Shell owner)
    {
        shell = owner;
        var width = S.Store.Layout("launcher", "width");
        Width = width;
        Height = S.Store.Layout("launcher", "defaultHeight");
        C.Prepare(this);
        Title = "DeskKit";

        box = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = S.Store.Layout("type", "input", "size"),
            FontWeight = FontWeights.Medium,
            FontFamily = Chrome.Font,
            Padding = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = C.Brush("accent"),
            FocusVisualStyle = null
        };
        hint = new TextBlock
        {
            IsHitTestVisible = false,
            FontSize = box.FontSize,
            FontFamily = Chrome.Font,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        capsuleText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var capsuleClose = C.IconButton(Glyph.Close, DropTrigger);
        capsuleClose.Width = 18;
        capsuleClose.Height = 18;
        capsuleClose.Margin = new Thickness(4, 0, 6, 0);
        var capsuleRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(capsuleClose, Dock.Right);
        capsuleRow.Children.Add(capsuleClose);
        capsuleRow.Children.Add(capsuleText);
        capsule = new Border
        {
            Height = 28,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = capsuleRow
        };
        clear = C.IconButton(Glyph.Close, ClearQuery);
        clear.Visibility = Visibility.Collapsed;

        var input = new Grid { Height = 64 };
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        var searchHost = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        searchHost.Name = "search";
        searchHost.Children.Add(C.SearchMark(16));
        Grid.SetColumn(searchHost, 0);
        Grid.SetColumn(capsule, 1);
        var fieldHost = new Grid();
        fieldHost.Children.Add(box);
        fieldHost.Children.Add(hint);
        Grid.SetColumn(fieldHost, 2);
        Grid.SetColumn(clear, 3);
        var gear = C.IconButton(Glyph.Gear, () => shell.ShowSettings());
        var close = C.IconButton(Glyph.Close, () => S.Hide());
        Grid.SetColumn(gear, 4);
        Grid.SetColumn(close, 5);
        input.Children.Add(searchHost);
        input.Children.Add(capsule);
        input.Children.Add(fieldHost);
        input.Children.Add(clear);
        input.Children.Add(gear);
        input.Children.Add(close);

        body = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body };
        footerHints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        footerAlert = new TextBlock { FontSize = 11, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        footerAlert.MouseLeftButtonUp += (_, _) => shell.ShowSettings("hotkeys");
        footerBar = new Border { Height = 32, Padding = new Thickness(12, 0, 12, 0), BorderThickness = new Thickness(0, 1, 0, 0) };
        var footer = new DockPanel();
        var brand = C.Label("DeskKit", 11, FontWeights.SemiBold, "text.tertiary");
        DockPanel.SetDock(brand, Dock.Right);
        footer.Children.Add(brand);
        footer.Children.Add(footerAlert);
        footer.Children.Add(footerHints);
        footerBar.Child = footer;

        var dock = new DockPanel();
        DockPanel.SetDock(input, Dock.Top);
        DockPanel.SetDock(footerBar, Dock.Bottom);
        dock.Children.Add(input);
        dock.Children.Add(footerBar);
        dock.Children.Add(scroll);
        root = C.Float(dock, "bg.float");
        Content = root;

        box.TextChanged += OnTextChanged;
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => OnBlur();
        Rebuild();
    }

    public void LockFrame()
    {
        if (!C.TryReadFrame(this, out var rect))
        {
            return;
        }

        lockedFrame = rect;
        C.ApplyFrame(this, rect, syncWpf: true);
    }

    public void UnlockFrame() => lockedFrame = null;

    public void EnforceFrame()
    {
        if (lockedFrame is not { } frame)
        {
            return;
        }

        C.ApplyFrame(this, frame, syncWpf: false);
    }

    public void Rebuild()
    {
        var triggered = S.TriggerWord.Length > 0;
        var raw = triggered ? S.Payload : S.Query;
        updating = true;
        if (box.Text != raw)
        {
            box.Text = raw;
            box.CaretIndex = box.Text.Length;
        }

        updating = false;
        capsule.Visibility = triggered ? Visibility.Visible : Visibility.Collapsed;
        capsuleText.Text = S.TriggerWord;
        capsuleText.Foreground = C.Brush("accent.hover");
        capsule.Background = C.Brush("accent.soft");
        capsule.BorderBrush = C.Brush("accent");
        hint.Text = triggered
            ? S.ResolveTrigger(S.TriggerWord)?.Placeholder ?? ""
            : S.Store.Text("launcher.placeholder");
        hint.Foreground = C.Brush("text.tertiary");
        hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.Foreground = C.Brush("text.primary");
        box.CaretBrush = C.Brush("accent");
        clear.Visibility = box.Text.Length == 0 && S.TriggerWord.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        LayoutFooter();

        body.Children.Clear();
        if (triggered)
        {
            DrawTrigger();
        }
        else if (S.Query.Trim().Length == 0)
        {
            DrawHome();
        }
        else if (S.Hits.Count == 0 || S.Machine.State == "A4")
        {
            DrawEmpty();
        }
        else
        {
            DrawResults();
        }

        EnforceFrame();

        var key = S.Query + "|" + S.TriggerWord;
        if (key != scrollKey)
        {
            scrollKey = key;
            scroll.ScrollToTop();
        }

        footerBar.Background = C.Brush("bg.subtle");
        footerBar.BorderBrush = C.Brush("border.default");
        C.PaintBackground(root, "bg.float");
    }

    void DrawHome()
    {
        var rows = S.RecentHits();
        var seen = rows.Select(hit => hit.Id).ToHashSet(StringComparer.Ordinal);
        var candidates = S.Index.Where(hit => hit.Kind == "app" && !seen.Contains(hit.Id)).Take(12).ToList();
        homeList = rows.Concat(candidates).ToList();
        var number = 1;
        Section("最近使用", rows.Count > 0 ? $"{rows.Count} 条" : "打开应用后会出现在这里");
        foreach (var hit in rows)
        {
            body.Children.Add(Row(hit, number++, selected: false, showEnter: false));
        }

        if (candidates.Count > 0)
        {
            Section("候选", $"{candidates.Count} 条");
            foreach (var hit in candidates)
            {
                body.Children.Add(Row(hit, number++, selected: false, showEnter: false));
            }
        }

        Section("推荐工具", "");
        Chips(
        [
            ("剪贴板历史", (Action)(() => shell.ShowTool("clipboard"))),
            ("计算器", (Action)(() => shell.ShowTool("calc"))),
            ("翻译", (Action)(() => shell.ShowTool("translate")))
        ]);
        Section("试试这些输入（拼音 / 首字母 / 算式 / 触发词）", "");
        var demos = new[] { "wx", "kzmb", "word", "fy hello", "123*4", "#3B6CF6", "100cny to usd", "color #3B6CF6" };
        Chips(demos.Select(demo => (demo, (Action)(() => Demo(demo)))).ToArray());
    }

    void DrawResults()
    {
        var labels = new Dictionary<string, string> { ["tool"] = "内置工具", ["app"] = "应用", ["file"] = "文件", ["url"] = "网址" };
        var shown = 0;
        foreach (var kind in new[] { "tool", "app", "file", "url" })
        {
            var rows = S.Hits.Where(hit => hit.Kind == kind).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            Section(labels[kind], $"{rows.Count} 条");
            foreach (var hit in rows)
            {
                var selected = shown == S.Selection;
                shown++;
                body.Children.Add(Row(hit, shown, selected, selected));
            }
        }
    }

    void DrawTrigger()
    {
        var word = S.TriggerWord;
        var payload = S.Payload;
        var resolved = S.ResolveTrigger(word);
        var head = new DockPanel { Margin = new Thickness(16, 8, 16, 4) };
        var exit = C.Soft("退出胶囊", DropTrigger);
        DockPanel.SetDock(exit, Dock.Right);
        head.Children.Add(exit);
        if (resolved != null)
        {
            var tag = C.Label(resolved.Value.Label, 11, FontWeights.SemiBold, "accent.hover");
            tag.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(tag, Dock.Right);
            head.Children.Add(tag);
        }

        head.Children.Add(C.Label($"触发词 {word} · 工具独占视图", 12, FontWeights.SemiBold, "text.secondary"));
        body.Children.Add(head);

        var card = new Border
        {
            Margin = new Thickness(16, 4, 16, 8),
            Padding = new Thickness(16),
            Background = C.Brush("bg.float"),
            BorderBrush = C.Brush("border.default"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
        var stack = new StackPanel();
        var hit = S.Hits.FirstOrDefault();
        var toolId = resolved?.ToolId ?? "";
        switch (toolId)
        {
            case "color":
                stack.Children.Add(C.Label("HEX ↔ RGB ↔ HSL · 输入 " + (payload.Length == 0 ? "#RRGGBB" : payload), 12, FontWeights.Normal, "text.tertiary"));
                var swatch = new Border { Height = 56, Margin = new Thickness(0, 10, 0, 10), CornerRadius = new CornerRadius(8), BorderBrush = C.Brush("border.default"), BorderThickness = new Thickness(1) };
                var hex = ColorHex(payload) ?? S.Store.Color("accent");
                swatch.Background = new SolidColorBrush(Chrome.Paint(hex));
                stack.Children.Add(swatch);
                stack.Children.Add(C.Label(string.IsNullOrEmpty(hit?.Title) ? "格式非法" : hit!.Title, 13, FontWeights.Medium, "text.primary"));
                break;
            case "clipboard":
                var needle = payload.ToLowerInvariant();
                var list = S.Clips.Where(clip => needle.Length == 0 || clip.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
                stack.Children.Add(C.Label($"剪贴板历史 · 点击复用（{list.Count} 条）", 12, FontWeights.Normal, "text.tertiary"));
                if (list.Count == 0)
                {
                    stack.Children.Add(C.Label("还没有复制记录，复制任意内容后这里会出现", 12, FontWeights.Normal, "text.secondary"));
                }

                foreach (var clip in list)
                {
                    var text = clip.Text;
                    var row = new Button
                    {
                        Content = text.Length > 48 ? text[..48] : text,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 6, 0, 0),
                        Height = 28,
                        Background = C.Brush("bg.subtle"),
                        BorderThickness = new Thickness(0),
                        Foreground = C.Brush("text.primary"),
                        Template = (ControlTemplate)C.Soft("x", () => { }).Template
                    };
                    row.Click += (_, _) =>
                    {
                        Session.Copy(text);
                        Log.Info("已复用剪贴板记录");
                        S.Hide();
                    };
                    stack.Children.Add(row);
                }

                break;
            default:
                var (sub, big, note) = toolId switch
                {
                    "translate" => ("原文", payload.Length == 0 ? "（输入待译文本）" : payload, hit?.Subtitle ?? ""),
                    "calc" => ("表达式 / 单位换算", payload.Length == 0 ? "（输入算式）" : payload, hit?.Subtitle ?? "支持四则运算与单位换算"),
                    "timestamp" => ("时间戳 ↔ 日期双向", payload.Length == 0 ? "（输入时间戳或日期）" : payload, "时区：系统当前时区"),
                    _ => ("工具", payload, "")
                };
                stack.Children.Add(C.Label(sub, 12, FontWeights.Normal, "text.tertiary"));
                stack.Children.Add(C.Label(big, 20, FontWeights.SemiBold, "text.primary"));
                stack.Children.Add(C.Label("结果", 12, FontWeights.Normal, "text.tertiary"));
                var result = C.Label(hit?.Title ?? "—", 22, FontWeights.SemiBold, "accent.hover");
                result.TextWrapping = TextWrapping.Wrap;
                stack.Children.Add(result);
                stack.Children.Add(C.Label(note, 12, FontWeights.Normal, "text.tertiary"));
                break;
        }

        card.Child = stack;
        body.Children.Add(card);
        var actions = new List<(string, Action)>();
        if (toolId.Length > 0)
        {
            actions.Add(($"打开「{Catalog.TitleOf(toolId)}」工具内页", () => shell.ShowTool(toolId)));
        }

        actions.Add(("Enter 复制结果", CopyTrigger));
        actions.Add(("Esc 退出触发词胶囊", DropTrigger));
        Chips(actions.ToArray());
    }

    void DrawEmpty()
    {
        var markHost = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        markHost.Children.Add(C.SearchMark(22));
        var circle = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(28),
            Background = C.Brush("bg.subtle"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
            Child = markHost
        };
        var stack = new StackPanel();
        stack.Children.Add(circle);
        var title = C.Label($"没有找到与 “{S.Query}” 相关的内容", 13, FontWeights.Normal, "text.secondary");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.TextAlignment = TextAlignment.Center;
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxWidth = 520;
        stack.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(C.Primary("用浏览器搜索", Browse));
        actions.Children.Add(C.Soft("清空重来", ClearQuery));
        actions.Children.Add(C.Soft("查看全部工具", () => shell.ShowTools()));
        stack.Children.Add(actions);
        var tip = C.Label($"{S.Store.FormatChord("windows", "escape")} 逐级回退 · 也可以试试 wx / kzmb / 123*4", 11, FontWeights.Normal, "text.tertiary");
        tip.HorizontalAlignment = HorizontalAlignment.Center;
        tip.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(tip);
        var plate = C.Brush("bg.float").Color;
        body.Children.Add(new Border
        {
            Margin = new Thickness(48, 8, 48, 4),
            Padding = new Thickness(16, 4, 16, 16),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(230, plate.R, plate.G, plate.B)),
            Child = stack
        });
    }

    void Section(string title, string count)
    {
        var row = new DockPanel { Margin = new Thickness(16, 8, 16, 2) };
        if (count.Length > 0)
        {
            var right = C.Label(count, 12, FontWeights.Normal, "text.tertiary");
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
        }

        row.Children.Add(C.Label(title, 12, FontWeights.SemiBold, "text.tertiary"));
        body.Children.Add(row);
    }

    Border Row(Hit hit, int index, bool selected, bool showEnter)
    {
        var height = S.Settings.Density == "compact" ? 40 : 48;
        var row = new Border
        {
            Height = height,
            Margin = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(8),
            Background = selected ? C.Brush("bg.selected") : Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (selected)
        {
            grid.Children.Add(new Border { Width = 3, Background = C.Brush("accent"), HorizontalAlignment = HorizontalAlignment.Left });
        }

        var icon = HitIcon(hit);
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        var text = new Grid { VerticalAlignment = VerticalAlignment.Center };
        text.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = C.Label(hit.Title, 14, FontWeights.Medium, "text.primary");
        Grid.SetRow(title, 0);
        text.Children.Add(title);
        if (height >= 48 && !string.IsNullOrEmpty(hit.Subtitle))
        {
            text.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var subtitle = C.Label(hit.Subtitle, 12, FontWeights.Normal, "text.tertiary");
            Grid.SetRow(subtitle, 1);
            text.Children.Add(subtitle);
        }

        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        var alt = S.Store.KeyLabel("windows", "alt");
        var badge = index <= 9 ? alt + index : index.ToString();
        var side = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        if (showEnter)
        {
            side.Children.Add(C.Label(S.Store.KeyLabel("windows", "enter"), 12, FontWeights.Normal, "text.tertiary"));
        }

        side.Children.Add(C.Keycap(badge));
        Grid.SetColumn(side, 3);
        grid.Children.Add(side);
        row.Child = grid;
        row.MouseLeftButtonUp += (_, _) => S.Execute(hit);
        row.MouseRightButtonUp += (_, e) =>
        {
            if (string.IsNullOrEmpty(hit.Path))
            {
                return;
            }

            shell.SuppressBlur = true;
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "打开文件夹" };
            open.Click += (_, _) => OpenFolder(hit);
            var copy = new MenuItem { Header = "复制路径" };
            copy.Click += (_, _) =>
            {
                var value = string.IsNullOrEmpty(hit.Path) ? hit.Title : hit.Path;
                Session.Copy(value);
                ShowCopied(value);
            };
            menu.Items.Add(open);
            menu.Items.Add(copy);
            menu.Closed += (_, _) => shell.SuppressBlur = false;
            menu.IsOpen = true;
            e.Handled = true;
        };
        return row;
    }

    UIElement HitIcon(Hit hit)
    {
        if (hit.Kind != "tool" && !string.IsNullOrEmpty(hit.Path))
        {
            var image = new Image { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            image.Source = Icons.File(hit.Path, 32, image);
            if (image.Source != null)
            {
                return image;
            }
        }

        return new TextBlock
        {
            Text = Glyph.ForTool(hit.ToolId ?? hit.Kind),
            FontFamily = Chrome.IconFont,
            FontSize = 18,
            Foreground = C.Brush("accent"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    void Chips((string Title, Action Run)[] items)
    {
        var wrap = new WrapPanel { Margin = new Thickness(16, 4, 16, 4) };
        foreach (var item in items)
        {
            var button = C.Soft(item.Title, item.Run);
            button.Margin = new Thickness(0, 0, 8, 8);
            wrap.Children.Add(button);
        }

        body.Children.Add(wrap);
    }

    void LayoutFooter()
    {
        if (footerReady && shell.HotkeyReady && footerHints.Children.Count > 0)
        {
            return;
        }

        footerReady = shell.HotkeyReady;
        footerHints.Children.Clear();
        var alt = S.Store.KeyLabel("windows", "alt");
        if (!shell.HotkeyReady)
        {
            footerHints.Visibility = Visibility.Collapsed;
            footerAlert.Visibility = Visibility.Visible;
            footerAlert.Text = $"{alt}+Space 未生效 · 点此去改键";
            footerAlert.Foreground = C.Brush("warning");
            return;
        }

        footerAlert.Visibility = Visibility.Collapsed;
        footerHints.Visibility = Visibility.Visible;
        var items = new[]
        {
            (S.Store.KeyLabel("windows", "up") + S.Store.KeyLabel("windows", "down"), "选择"),
            (S.Store.KeyLabel("windows", "enter"), "执行"),
            ($"{alt}+1–9", "打开"),
            (S.Store.KeyLabel("windows", "tab"), "补全"),
            (S.Store.KeyLabel("windows", "esc"), "回退")
        };
        foreach (var item in items)
        {
            footerHints.Children.Add(C.Keycap(item.Item1));
            var caption = C.Label(item.Item2, 11, FontWeights.Normal, "text.tertiary");
            caption.Margin = new Thickness(0, 0, 12, 0);
            footerHints.Children.Add(caption);
        }
    }

    void ShowCopied(string text)
    {
        footerHints.Visibility = Visibility.Collapsed;
        footerAlert.Visibility = Visibility.Visible;
        footerAlert.Text = "已复制 " + text;
        footerAlert.Foreground = C.Brush("text.secondary");
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            footerReady = false;
            LayoutFooter();
        };
        timer.Start();
    }

    void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (updating)
        {
            return;
        }

        var typed = box.Text;
        if (S.TriggerWord.Length == 0 && Split(typed) is (string word, string rest))
        {
            S.Query = word + " " + rest;
            updating = true;
            box.Text = rest;
            box.CaretIndex = box.Text.Length;
            updating = false;
        }
        else if (S.TriggerWord.Length > 0)
        {
            S.Query = S.TriggerWord + " " + typed;
        }
        else
        {
            S.Query = typed;
        }

        S.Search();
        Rebuild();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!box.IsKeyboardFocusWithin)
        {
            box.Focus();
        }

        switch (key)
        {
            case Key.Down:
                if (S.Hits.Count > 0)
                {
                    S.Selection = Math.Min(S.Hits.Count - 1, S.Selection + 1);
                    Rebuild();
                }

                e.Handled = true;
                return;
            case Key.Up:
                if (S.Hits.Count > 0)
                {
                    S.Selection = Math.Max(0, S.Selection - 1);
                    Rebuild();
                }

                e.Handled = true;
                return;
            case Key.Enter:
                if (S.TriggerWord.Length > 0 && S.Hits.Count > 0)
                {
                    S.Execute(S.Hits[0]);
                }
                else
                {
                    S.ExecuteSelection();
                }

                e.Handled = true;
                return;
            case Key.Escape:
                if (shell.Tools.IsVisible)
                {
                    shell.CloseTool();
                }
                else if (S.TriggerWord.Length > 0)
                {
                    DropTrigger();
                }
                else if (S.Query.Length > 0)
                {
                    ClearQuery();
                }
                else
                {
                    S.Escape();
                }

                e.Handled = true;
                return;
            case Key.Tab:
                shell.ShowTools();
                e.Handled = true;
                return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt && Digit(key) is int digit)
        {
            var source = S.Query.Length == 0 && S.TriggerWord.Length == 0 ? homeList : S.Hits;
            if (digit >= 1 && digit <= source.Count)
            {
                Log.Info($"Alt+{digit} 打开第 {digit} 条");
                S.Execute(source[digit - 1]);
            }

            e.Handled = true;
        }
    }

    async void OnBlur()
    {
        if (!S.Settings.BlurHide || !IsVisible || shell.SuppressBlur)
        {
            return;
        }

        await Task.Delay(150);
        if (!IsActive && IsVisible && !shell.SuppressBlur)
        {
            S.Hide();
        }
    }

    void ClearQuery()
    {
        S.Query = "";
        S.TriggerWord = "";
        S.Payload = "";
        S.Search();
        Rebuild();
        Log.Info("已清空输入");
    }

    void DropTrigger()
    {
        S.TriggerWord = "";
        S.Payload = "";
        S.Query = "";
        S.Search();
        Rebuild();
        Log.Info("退出触发词胶囊");
    }

    void Browse()
    {
        var url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(S.Query);
        Session.Open(url);
        S.Hide();
    }

    void Demo(string query)
    {
        S.Query = query;
        S.Search();
        Rebuild();
        Log.Info($"演示查询 {query}");
    }

    void CopyTrigger()
    {
        if (S.Hits.FirstOrDefault()?.CopyText is string text)
        {
            Session.Copy(text);
            Log.Info("已复制触发词结果");
            S.Hide();
        }
    }

    static void OpenFolder(Hit hit)
    {
        var path = hit.Path;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Log.Warn($"路径不存在 {path}");
            return;
        }

        var folder = Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path) ?? path;
        Session.Open(folder);
        Log.Info($"打开文件夹 {folder}");
    }

    (string Word, string Tail)? Split(string text)
    {
        var space = text.IndexOf(' ');
        if (space <= 0)
        {
            return null;
        }

        var word = text[..space];
        if (S.ResolveTrigger(word) == null)
        {
            return null;
        }

        return (word, text[(space + 1)..]);
    }

    static int? Digit(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        Key.D7 or Key.NumPad7 => 7,
        Key.D8 or Key.NumPad8 => 8,
        Key.D9 or Key.NumPad9 => 9,
        _ => null
    };

    static string? ColorHex(string input)
    {
        var rgb = DeskKit.Core.ColorConvert.Parse(input);
        return rgb == null ? null : $"#{rgb.Value.R:X2}{rgb.Value.G:X2}{rgb.Value.B:X2}";
    }

    public void FocusSearch(string reason)
    {
        Activate();
        box.Focus();
        Keyboard.Focus(box);
        Log.Info($"输入框聚焦[{reason}]");
    }
}
