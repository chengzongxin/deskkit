using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskKit.Core;
using DeskKit.Platform;
using Microsoft.Win32;

namespace DeskKit.App;

public sealed class GroupsWindow : Window
{
    readonly Shell shell;
    string nav = "all";
    bool edit;
    string search = "";
    string? renaming;
    readonly HashSet<string> folded = new(StringComparer.Ordinal);
    bool rendering;
    bool resizing;
    Border? dropVeil;
    TextBlock? dropTitle;
    Border? dropHighlight;
    Chrome C => shell.Chrome;
    Session S => shell.Session;

    public GroupsWindow(Shell owner)
    {
        shell = owner;
        Title = "DeskKit 快捷启动";
        Width = S.Store.Layout("windows", "panel", "width");
        Height = S.Store.Layout("windows", "panel", "height");
        MinWidth = 760;
        MinHeight = 480;
        C.Prepare(this);
        AllowDrop = true;
        DragOver += OnDragOver;
        DragLeave += (_, _) => HideDropHint();
        Drop += OnDrop;
        PreviewMouseMove += (_, e) => UpdateResizeCursor(e.GetPosition(this));
        PreviewMouseLeftButtonDown += OnPanelMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Escape)
        {
            return;
        }

        if (renaming != null)
        {
            renaming = null;
            Render();
            e.Handled = true;
            Log.Info("Esc 取消重命名分组");
            return;
        }

        Hide();
        e.Handled = true;
        Log.Info("Esc 关闭快捷启动");
    }

    public void Render()
    {
        rendering = true;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(S.Store.Layout("windows", "panelFooterHeight")) });

        var headerDock = new DockPanel { Margin = new Thickness(16, 0, 8, 0) };
        EnableDrag(headerDock);
        var close = C.IconButton(Glyph.Close, Hide);
        var gear = C.IconButton(Glyph.Gear, () => shell.ShowSettings());
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(gear, Dock.Right);
        headerDock.Children.Add(close);
        headerDock.Children.Add(gear);
        var box = new TextBox
        {
            Width = 168,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 10, 0),
            Text = search,
            ToolTip = "搜索快捷项",
            Margin = new Thickness(8, 0, 8, 0),
            Background = C.Brush("bg.input"),
            Foreground = C.Brush("text.primary"),
            BorderBrush = C.Brush("border.strong"),
            BorderThickness = new Thickness(1)
        };
        box.TextChanged += (_, _) =>
        {
            if (rendering) return;
            search = box.Text;
            rendering = false;
            Render();
        };
        DockPanel.SetDock(box, Dock.Right);
        headerDock.Children.Add(box);
        var editButton = C.Soft(edit ? "✓ 完成" : "✎ 编辑", () => { edit = !edit; Render(); });
        var addTop = C.Soft("+ 添加", AddShortcut);
        DockPanel.SetDock(editButton, Dock.Right);
        DockPanel.SetDock(addTop, Dock.Right);
        headerDock.Children.Add(editButton);
        headerDock.Children.Add(addTop);
        headerDock.Children.Add(C.Label("DeskKit 快捷启动", 16, FontWeights.SemiBold, "text.primary"));
        var header = new Border
        {
            Background = C.Glass("bg.float", 0.38),
            BorderBrush = C.Brush("border.default"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = headerDock
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S.Store.Layout("windows", "navWidth")) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var navList = new StackPanel { Margin = new Thickness(8, 12, 8, 0) };
        EnableDrag(navList);
        navList.Children.Add(C.Label("分组", 11, FontWeights.SemiBold, "text.tertiary"));
        Nav(navList, "all", "全部", S.Library.Items.Count, null);
        Nav(navList, "fav", "收藏", 0, S.Store.Color("accent"));
        foreach (var group in S.Library.Groups)
        {
            Nav(navList, group.Id, group.Name, group.ItemIds.Count, group.Color);
        }

        navList.Children.Add(C.Soft("新建分组", AddGroup));
        var nav = new Border
        {
            Background = C.Glass("bg.float", 0.32),
            BorderBrush = C.Brush("border.default"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = navList
        };
        Grid.SetColumn(nav, 0);
        body.Children.Add(nav);
        var canvas = new StackPanel { Margin = new Thickness(8) };
        DrawGroups(canvas);
        var scroll = new ScrollViewer
        {
            Content = canvas,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent
        };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footGrid = new Grid();
        footGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S.Store.Layout("windows", "navWidth")) });
        footGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var add = edit ? C.Primary("✓ 完成", () => { edit = !edit; Render(); }) : C.Soft(S.Store.Text("empty.panelAction"), AddShortcut);
        add.Margin = new Thickness(8, 0, 16, 0);
        add.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(add, 2);
        var tip = C.Label($"把应用、文件或文件夹拖到这里即可添加 · {shell.PanelHotkeyText()} 唤出 · 共 {S.Library.Items.Count} 项", 12, FontWeights.Normal, "text.tertiary");
        tip.TextTrimming = TextTrimming.CharacterEllipsis;
        tip.VerticalAlignment = VerticalAlignment.Center;
        tip.Margin = new Thickness(16, 0, 8, 0);
        Grid.SetColumn(tip, 1);
        footGrid.Children.Add(tip);
        footGrid.Children.Add(add);
        EnableDrag(footGrid);
        var foot = new Border
        {
            Background = C.Glass("bg.float", 0.38),
            BorderBrush = C.Brush("border.default"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footGrid
        };
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        dropTitle = C.Label("松手添加到分组", 16, FontWeights.SemiBold, "text.primary");
        dropTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var dropSub = C.Label("应用、文件、文件夹或网址", 12, FontWeights.Normal, "text.secondary");
        dropSub.HorizontalAlignment = HorizontalAlignment.Center;
        dropSub.Margin = new Thickness(0, 6, 0, 0);
        var badge = new Border
        {
            Width = 380,
            Padding = new Thickness(16, 22, 16, 22),
            CornerRadius = new CornerRadius(12),
            Background = C.Glass("bg.float", 0.55),
            BorderBrush = C.Brush("accent"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Children = { dropTitle, dropSub } }
        };
        dropVeil = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(72, 0, 0, 0)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = badge
        };
        Grid.SetRowSpan(dropVeil, 3);
        root.Children.Add(dropVeil);
        Content = C.Float(root, "bg.canvas");
        rendering = false;
        Log.Info($"打开快捷启动面板 {Width:0}x{Height:0}");
    }

    void Nav(Panel nav, string id, string title, int count, string? color)
    {
        var on = navId(id);
        var row = new Border
        {
            Height = 34,
            Margin = new Thickness(0, 4, 0, 0),
            CornerRadius = new CornerRadius(8),
            Background = on ? C.Brush("accent.soft") : Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        var grid = new DockPanel { Margin = new Thickness(10, 0, 8, 0) };
        var number = C.Label(count.ToString(), 11, FontWeights.Normal, "text.tertiary");
        DockPanel.SetDock(number, Dock.Right);
        grid.Children.Add(number);
        if (color != null)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Chrome.Paint(color)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Left);
            grid.Children.Add(dot);
        }

        grid.Children.Add(C.Label(title, 13, on ? FontWeights.SemiBold : FontWeights.Normal, on ? "accent.hover" : "text.secondary"));
        row.Tag = "nav";
        row.Child = grid;
        row.MouseLeftButtonUp += (_, _) => { this.nav = id; Render(); };
        nav.Children.Add(row);
    }

    bool navId(string id) => nav == id;

    void DrawGroups(Panel canvas)
    {
        IEnumerable<Group> groups = nav switch
        {
            "all" => S.Library.Groups,
            "fav" => [new Group { Id = "fav", Name = "收藏", Color = S.Store.Color("accent"), ItemIds = [] }],
            _ => S.Library.Groups.Where(group => group.Id == nav)
        };
        foreach (var group in groups)
        {
            var items = group.ItemIds.Select(id => S.Library.Items.FirstOrDefault(item => item.Id == id)).Where(item => item != null).Cast<Shortcut>()
                .Where(item => search.Length == 0 || item.Title.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            var isFolded = folded.Contains(group.Id);
            var card = new Border
            {
                Background = C.Glass("bg.float", 0.42),
                BorderBrush = C.Brush("border.default"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(8)
            };
            var stack = new StackPanel();
            var head = new DockPanel();
            if (group.Id != "fav")
            {
                var fold = C.IconButton(isFolded ? Glyph.Right : Glyph.Down, () =>
                {
                    if (!folded.Add(group.Id)) folded.Remove(group.Id);
                    Render();
                });
                DockPanel.SetDock(fold, Dock.Right);
                head.Children.Add(fold);
                var rename = C.IconButton(Glyph.Edit, () => { renaming = group.Id; Render(); });
                rename.Margin = new Thickness(0, 0, 4, 0);
                DockPanel.SetDock(rename, Dock.Right);
                head.Children.Add(rename);
            }

            var bar = new Border { Width = 3, Height = 14, Background = new SolidColorBrush(Chrome.Paint(group.Color)), CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(bar, Dock.Left);
            head.Children.Add(bar);
            if (renaming == group.Id && group.Id != "fav")
            {
                var titleRow = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
                var count = C.Label($"{items.Count} 项", 11, FontWeights.Normal, "text.tertiary");
                count.VerticalAlignment = VerticalAlignment.Center;
                count.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(count, Dock.Right);
                titleRow.Children.Add(count);
                var field = new TextBox
                {
                    Text = group.Name,
                    MaxWidth = 180,
                    Height = 24,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Background = C.Brush("bg.input"),
                    Foreground = C.Brush("text.primary"),
                    BorderBrush = C.Brush("border.strong"),
                    Padding = new Thickness(6, 0, 6, 0)
                };
                var groupId = group.Id;
                field.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        renaming = null;
                        Render();
                        e.Handled = true;
                        return;
                    }

                    if (e.Key != Key.Enter)
                    {
                        return;
                    }

                    CommitRename(groupId, field.Text);
                    e.Handled = true;
                };
                field.LostFocus += (_, _) => CommitRename(groupId, field.Text);
                field.Loaded += (_, _) => field.Focus();
                titleRow.Children.Add(field);
                head.Children.Add(titleRow);
            }
            else
            {
                var titleRow = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
                var count = C.Label($"{items.Count} 项", 11, FontWeights.Normal, "text.tertiary");
                count.VerticalAlignment = VerticalAlignment.Center;
                count.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(count, Dock.Right);
                titleRow.Children.Add(count);
                var name = C.Label(group.Name, 13, FontWeights.SemiBold, "text.primary");
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.VerticalAlignment = VerticalAlignment.Center;
                titleRow.Children.Add(name);
                head.Children.Add(titleRow);
            }
            stack.Children.Add(head);
            if (!isFolded)
            {
                if (items.Count == 0)
                {
                    var empty = C.Label(S.Store.Text("empty.panel"), 13, FontWeights.Normal, "text.secondary");
                    empty.HorizontalAlignment = HorizontalAlignment.Center;
                    empty.Margin = new Thickness(0, 24, 0, 8);
                    stack.Children.Add(empty);
                    var add = C.Primary(S.Store.Text("empty.panelAction"), AddShortcut);
                    add.HorizontalAlignment = HorizontalAlignment.Center;
                    stack.Children.Add(add);
                }
                else
                {
                    var wrap = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
                    foreach (var item in items)
                    {
                        wrap.Children.Add(Cell(item));
                    }

                    if (edit)
                    {
                        var add = new Border { Width = 80, Height = 80, Margin = new Thickness(4), CornerRadius = new CornerRadius(8), BorderBrush = C.Brush("border.strong"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                        var plus = C.Label("添加", 12, FontWeights.Normal, "text.tertiary");
                        plus.HorizontalAlignment = HorizontalAlignment.Center;
                        plus.VerticalAlignment = VerticalAlignment.Center;
                        add.Child = plus;
                        add.MouseLeftButtonUp += (_, _) => AddShortcut();
                        wrap.Children.Add(add);
                    }

                    stack.Children.Add(wrap);
                }
            }

            card.Tag = group.Id;
            card.Child = stack;
            canvas.Children.Add(card);
        }
    }

    UIElement Cell(Shortcut item)
    {
        var cell = new Grid { Width = 80, Height = 80, Margin = new Thickness(4), Cursor = Cursors.Hand };
        var image = Icons.File(item.Path, 40);
        UIElement icon = image != null
            ? new Image { Source = image, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) }
            : new TextBlock { Text = item.Kind == "url" ? Glyph.Link : Glyph.App, FontFamily = Chrome.IconFont, FontSize = 22, Foreground = C.Brush("accent"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        var label = C.Label(item.Title, 11, FontWeights.Normal, "text.secondary");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Margin = new Thickness(2, 0, 2, 6);
        label.TextAlignment = TextAlignment.Center;
        cell.Children.Add(icon);
        cell.Children.Add(label);
        if (edit)
        {
            var del = C.IconButton(Glyph.Close, () =>
            {
                S.Library.Items.RemoveAll(entry => entry.Id == item.Id);
                foreach (var group in S.Library.Groups) group.ItemIds.RemoveAll(id => id == item.Id);
                S.Save();
                Render();
            });
            del.HorizontalAlignment = HorizontalAlignment.Right;
            del.VerticalAlignment = VerticalAlignment.Top;
            cell.Children.Add(del);
        }

        cell.MouseLeftButtonUp += (_, e) =>
        {
            if (edit) return;
            try
            {
                Session.Open(item.Path);
                S.TouchRecentFromPath(item.Path, item.Kind, item.Title);
            }
            catch (Exception ex) { Log.Warn($"打开失败 {ex.Message}"); }
            e.Handled = true;
        };
        return cell;
    }

    void CommitRename(string id, string name)
    {
        if (renaming != id) return;
        renaming = null;
        S.RenameGroup(id, name);
        Render();
    }

    void AddGroup()
    {
        var colors = S.Store.GroupColors;
        var color = colors.Count > 3 ? colors[3] : "#9B6CF6";
        var id = "g" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        S.Library.Groups.Add(new Group { Id = id, Name = "新分组", Color = color, ItemIds = [] });
        S.Save();
        nav = id;
        Render();
        Log.Info($"新建分组 {id} 色 {color}");
    }

    void AddShortcut()
    {
        shell.SuppressBlur = true;
        // 文件名先放一个占位。只点文件夹再按「添加」时，返回值会带着这个占位，从而得到文件夹路径。
        var dialog = new OpenFileDialog
        {
            Title = "选择应用、文件或文件夹",
            Multiselect = true,
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = "选择此项"
        };
        var ok = dialog.ShowDialog() == true;
        shell.SuppressBlur = false;
        if (!ok)
        {
            return;
        }

        var groupId = DropTarget(null);
        foreach (var name in dialog.FileNames)
        {
            var path = name.EndsWith("选择此项", StringComparison.Ordinal) ? System.IO.Path.GetDirectoryName(name) : name;
            if (!string.IsNullOrWhiteSpace(path))
            {
                S.AddShortcut(path, groupId);
            }
        }

        Render();
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        var allowed = CanDrop(e);
        e.Effects = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (!allowed)
        {
            HideDropHint();
            return;
        }

        var groupId = DropTarget(GroupUnder(e));
        ShowDropHint(groupId);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        var groupId = DropTarget(GroupUnder(e));
        var added = 0;
        var total = 0;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            total = files.Length;
            foreach (var file in files)
            {
                if (S.AddShortcut(file, groupId))
                {
                    added++;
                }
            }
        }
        else if (DroppedText(e) is string text)
        {
            total = 1;
            if (S.AddUrl(text, groupId))
            {
                added++;
            }
        }

        HideDropHint();
        var name = S.Library.Groups.FirstOrDefault(group => group.Id == groupId)?.Name ?? groupId;
        Log.Info($"拖入完成，放入「{name}」{added} 个，共 {total} 个");
        Render();
    }

    void EnableDrag(UIElement element)
    {
        element.MouseLeftButtonDown += (_, e) =>
        {
            if (resizing || IsInteractive(e.OriginalSource as DependencyObject))
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            RememberFrame();
        };
    }

    static bool IsInteractive(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button or TextBox)
            {
                return true;
            }

            if (source is FrameworkElement element && element.Tag as string == "nav")
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    void OnPanelMouseDown(object sender, MouseButtonEventArgs e)
    {
        var edge = ResizeEdge(e.GetPosition(this));
        if (edge.Length == 0)
        {
            return;
        }

        StartResize(edge, e.GetPosition(this));
        e.Handled = true;
    }

    void StartResize(string edge, Point start)
    {
        resizing = true;
        var originLeft = Left;
        var originTop = Top;
        var originWidth = ActualWidth;
        var originHeight = ActualHeight;
        var screenStart = PointToScreen(start);
        CaptureMouse();
        MouseEventHandler? move = null;
        MouseButtonEventHandler? up = null;
        move = (_, e) =>
        {
            var now = PointToScreen(e.GetPosition(this));
            var dx = now.X - screenStart.X;
            var dy = now.Y - screenStart.Y;
            var width = originWidth;
            var height = originHeight;
            var left = originLeft;
            var top = originTop;
            if (edge.Contains('r')) width = Math.Max(MinWidth, originWidth + dx);
            if (edge.Contains('l'))
            {
                width = Math.Max(MinWidth, originWidth - dx);
                left = originLeft + originWidth - width;
            }

            if (edge.Contains('b')) height = Math.Max(MinHeight, originHeight + dy);
            if (edge.Contains('t'))
            {
                height = Math.Max(MinHeight, originHeight - dy);
                top = originTop + originHeight - height;
            }

            Left = left;
            Top = top;
            Width = width;
            Height = height;
        };
        up = (_, _) =>
        {
            if (move != null) MouseMove -= move;
            if (up != null) MouseLeftButtonUp -= up;
            ReleaseMouseCapture();
            resizing = false;
            Cursor = Cursors.Arrow;
            RememberFrame();
            Render();
        };
        MouseMove += move;
        MouseLeftButtonUp += up;
    }

    void UpdateResizeCursor(Point point)
    {
        if (resizing)
        {
            return;
        }

        Cursor = ResizeEdge(point) switch
        {
            "l" or "r" => Cursors.SizeWE,
            "t" or "b" => Cursors.SizeNS,
            "tl" or "br" => Cursors.SizeNWSE,
            "tr" or "bl" => Cursors.SizeNESW,
            _ => Cursors.Arrow
        };
    }

    string ResizeEdge(Point point)
    {
        const double edge = 7;
        var left = point.X <= edge;
        var right = point.X >= ActualWidth - edge;
        var top = point.Y <= edge;
        var bottom = point.Y >= ActualHeight - edge;
        var horizontal = left ? "l" : right ? "r" : "";
        var vertical = top ? "t" : bottom ? "b" : "";
        return vertical + horizontal;
    }

    void RememberFrame()
    {
        S.Settings.PanelPlacedByUser = true;
        S.Settings.PanelX = Left;
        S.Settings.PanelY = Top;
        S.Settings.PanelWidth = Width;
        S.Settings.PanelHeight = Height;
        S.Save();
        Log.Info($"记住快捷启动位置 {Left:0},{Top:0} 尺寸 {Width:0}x{Height:0}");
    }

    static bool CanDrop(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) || DroppedText(e) != null;

    static string? DroppedText(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetData(DataFormats.Text) is not string text)
        {
            return null;
        }

        text = text.Trim();
        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? text
            : null;
    }

    string? GroupUnder(DragEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(this, e.GetPosition(this))?.VisualHit as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement element && element.Tag is string tag && S.Library.Groups.Any(group => group.Id == tag))
            {
                return tag;
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }

    string DropTarget(string? hover)
    {
        if (hover != null && hover != "fav" && S.Library.Groups.Any(group => group.Id == hover))
        {
            return hover;
        }

        if (nav is not ("all" or "fav") && S.Library.Groups.Any(group => group.Id == nav))
        {
            return nav;
        }

        if (S.Library.Groups.Any(group => group.Id == "ungrouped"))
        {
            return "ungrouped";
        }

        return S.Library.Groups.FirstOrDefault()?.Id ?? "ungrouped";
    }

    void ShowDropHint(string groupId)
    {
        if (dropVeil == null || dropTitle == null)
        {
            return;
        }

        var name = S.Library.Groups.FirstOrDefault(group => group.Id == groupId)?.Name ?? "未分组";
        dropTitle.Text = $"松手添加到「{name}」";
        dropVeil.Visibility = Visibility.Visible;
        HighlightGroup(groupId);
    }

    void HideDropHint()
    {
        if (dropVeil != null)
        {
            dropVeil.Visibility = Visibility.Collapsed;
        }

        HighlightGroup(null);
    }

    void HighlightGroup(string? id)
    {
        if (dropHighlight != null)
        {
            dropHighlight.BorderBrush = C.Brush("border.default");
            dropHighlight.BorderThickness = new Thickness(1);
        }

        dropHighlight = null;
        if (id == null || Content is not DependencyObject root)
        {
            return;
        }

        var next = FindTagged(root, id);
        if (next == null)
        {
            return;
        }

        next.BorderBrush = C.Brush("accent");
        next.BorderThickness = new Thickness(2);
        dropHighlight = next;
    }

    static Border? FindTagged(DependencyObject node, string tag)
    {
        if (node is Border border && border.Tag as string == tag)
        {
            return border;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var found = FindTagged(VisualTreeHelper.GetChild(node, i), tag);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
