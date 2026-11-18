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
    readonly HashSet<string> folded = new(StringComparer.Ordinal);
    bool rendering;
    Chrome C => shell.Chrome;
    Session S => shell.Session;

    public GroupsWindow(Shell owner)
    {
        shell = owner;
        Title = "DeskKit 快捷启动";
        Width = S.Store.Layout("windows", "panel", "width");
        Height = S.Store.Layout("windows", "panel", "height");
        C.Prepare(this);
        AllowDrop = true;
        Drop += OnDrop;
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
        var gear = C.IconButton(Glyph.Gear, () => shell.ShowSettings());
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(gear, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(gear);
        var box = new TextBox { Width = 160, Height = 28, VerticalAlignment = VerticalAlignment.Center, Text = search, Margin = new Thickness(8, 0, 8, 0) };
        box.TextChanged += (_, _) =>
        {
            if (rendering) return;
            search = box.Text;
            rendering = false;
            Render();
        };
        DockPanel.SetDock(box, Dock.Right);
        header.Children.Add(box);
        var editButton = C.Soft(edit ? "完成" : "编辑", () => { edit = !edit; Render(); });
        var addTop = C.Soft("添加", AddShortcut);
        DockPanel.SetDock(editButton, Dock.Right);
        DockPanel.SetDock(addTop, Dock.Right);
        header.Children.Add(editButton);
        header.Children.Add(addTop);
        header.Children.Add(C.Label("DeskKit 快捷启动", 16, FontWeights.SemiBold, "text.primary"));
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S.Store.Layout("windows", "navWidth")) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nav = new StackPanel { Margin = new Thickness(8, 12, 8, 0), Background = C.Brush("bg.float") };
        nav.Children.Add(C.Label("分组", 11, FontWeights.SemiBold, "text.tertiary"));
        Nav(nav, "all", "全部", S.Library.Items.Count, null);
        Nav(nav, "fav", "收藏", 0, S.Store.Color("accent"));
        foreach (var group in S.Library.Groups)
        {
            Nav(nav, group.Id, group.Name, group.ItemIds.Count, group.Color);
        }

        nav.Children.Add(C.Soft("新建分组", AddGroup));
        Grid.SetColumn(nav, 0);
        body.Children.Add(nav);
        var canvas = new StackPanel { Margin = new Thickness(8) };
        DrawGroups(canvas);
        var scroll = new ScrollViewer { Content = canvas, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var foot = new DockPanel { Margin = new Thickness(16, 0, 12, 0), Background = C.Brush("bg.float") };
        var add = edit ? C.Primary("完成", () => { edit = !edit; Render(); }) : C.Soft("添加应用", AddShortcut);
        DockPanel.SetDock(add, Dock.Right);
        foot.Children.Add(add);
        foot.Children.Add(C.Label($"把应用 / 文件 / 网址拖到这里即可添加 · 共 {S.Library.Items.Count} 项 · 已自动保存", 12, FontWeights.Normal, "text.tertiary"));
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
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
                Background = C.Brush("bg.float"),
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
                if (edit && group.Id != "ungrouped")
                {
                    var remove = C.Soft("删除分组", () => { S.DeleteGroup(group.Id); Render(); });
                    DockPanel.SetDock(remove, Dock.Right);
                    head.Children.Add(remove);
                }
            }

            var bar = new Border { Width = 3, Height = 14, Background = new SolidColorBrush(Chrome.Paint(group.Color)), CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(bar, Dock.Left);
            head.Children.Add(bar);
            head.Children.Add(C.Label($"{group.Name}    {items.Count} 项", 13, FontWeights.SemiBold, "text.primary"));
            stack.Children.Add(head);
            if (!isFolded)
            {
                if (items.Count == 0)
                {
                    var empty = C.Label(S.Store.Text("empty.panel"), 13, FontWeights.Normal, "text.secondary");
                    empty.HorizontalAlignment = HorizontalAlignment.Center;
                    empty.Margin = new Thickness(0, 24, 0, 8);
                    stack.Children.Add(empty);
                    var add = C.Primary("添加应用", AddShortcut);
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
            try { Session.Open(item.Path); }
            catch (Exception ex) { Log.Warn($"打开失败 {ex.Message}"); }
            e.Handled = true;
        };
        return cell;
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
        var dialog = new OpenFileDialog { Multiselect = true, Title = "添加" };
        var ok = dialog.ShowDialog() == true;
        shell.SuppressBlur = false;
        if (!ok) return;
        var groupId = nav is "all" or "fav" ? S.Library.Groups.FirstOrDefault()?.Id ?? "ungrouped" : nav;
        foreach (var name in dialog.FileNames) S.AddShortcut(name, groupId);
        Render();
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        var groupId = nav is "all" or "fav" ? S.Library.Groups.FirstOrDefault()?.Id ?? "ungrouped" : nav;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files) S.AddShortcut(file, groupId);
        }
        else if (e.Data.GetDataPresent(DataFormats.Text) && e.Data.GetData(DataFormats.Text) is string text && text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            S.AddUrl(text.Trim(), groupId);
        }

        Render();
    }
}
