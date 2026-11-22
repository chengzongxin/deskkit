using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
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
    bool navInsertAfter;
    bool menuUsed;
    bool navReorderArmed;
    string? navPressId;
    string? navReorderTarget;
    Border? navPressRow;
    Point navPressPoint;
    DispatcherTimer? navArmTimer;
    int blurGeneration;
    bool externalDragOver;
    int externalDragTick;
    Border? navDropHint;
    Border? dropVeil;
    TextBlock? dropTitle;
    Border? dropHighlight;
    ScrollViewer? contentScroller;
    ScrollViewer? navScroller;
    int wheelBucket;
    bool snapNav;
    Border? dragCaption;
    TextBlock? dragCaptionText;
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
        Deactivated += (_, _) => OnBlur();
        Activated += (_, _) => blurGeneration++;
        AllowDrop = true;
        DragOver += OnDragOver;
        DragLeave += (_, _) =>
        {
            externalDragOver = false;
            HideDropHint();
            HideDragCaption();
        };
        Drop += OnDrop;
        PreviewMouseMove += (_, e) => UpdateResizeCursor(e.GetPosition(this));
        PreviewMouseLeftButtonDown += OnPanelMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewKeyUp += SwallowAlt;
    }

    static void SwallowAlt(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
        }
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }

        var typing = renaming != null || Keyboard.FocusedElement is TextBox;
        if (key == Key.Escape)
        {
            if (renaming != null)
            {
                renaming = null;
                Render();
                e.Handled = true;
                Log.Info("Esc 取消重命名分组");
                return;
            }

            Dismiss();
            e.Handled = true;
            Log.Info("Esc 关闭快捷启动");
            return;
        }

        if (typing)
        {
            return;
        }

        if (key == Key.Down)
        {
            MoveNav(1);
            e.Handled = true;
            return;
        }

        if (key == Key.Up)
        {
            MoveNav(-1);
            e.Handled = true;
            return;
        }

        if (key == Key.Enter)
        {
            OpenFirstShortcut();
            e.Handled = true;
        }
    }

    /// <summary>滚轮和上下方向键一样，一次一格切换分组。触控板的小幅度会先攒够再换。</summary>
    void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (renaming != null || externalDragOver)
        {
            return;
        }

        e.Handled = true;
        wheelBucket += e.Delta;
        var steps = 0;
        while (wheelBucket >= 120)
        {
            wheelBucket -= 120;
            steps--;
        }

        while (wheelBucket <= -120)
        {
            wheelBucket += 120;
            steps++;
        }

        if (steps != 0)
        {
            MoveNav(steps);
        }
    }

    public void Dismiss()
    {
        RememberFrame();
        C.FadeOut(this, Hide);
    }

    async void OnBlur()
    {
        if (!S.Settings.BlurHide || !IsVisible || shell.SuppressBlur)
        {
            return;
        }

        var token = ++blurGeneration;
        if (HoldingForDrag())
        {
            // 点别的窗口时，失焦发生在鼠标按下的那一下。拖文件也是从这一下开始的。
            Log.Info("鼠标仍按着，快捷启动先留着，方便拖进来");
        }

        while (token == blurGeneration && IsVisible && !IsActive && !shell.SuppressBlur)
        {
            if (HoldingForDrag())
            {
                await Task.Delay(40);
                continue;
            }

            await Task.Delay(150);
            if (token != blurGeneration || IsActive || !IsVisible || shell.SuppressBlur || HoldingForDrag())
            {
                if (HoldingForDrag())
                {
                    continue;
                }

                return;
            }

            Log.Info("点击快捷启动窗口外，隐藏面板");
            Dismiss();
            return;
        }
    }

    /// <summary>左键还按着，或文件正拖在面板上。这两种时候都不能当成「点外面」。</summary>
    bool HoldingForDrag()
    {
        if (NativeMethods.IsLeftButtonDown())
        {
            return true;
        }

        return externalDragOver && unchecked(Environment.TickCount - externalDragTick) < 600;
    }

    void KeepOpenWhileMenu(ContextMenu menu)
    {
        menu.Opened += (_, _) => shell.SuppressBlur = true;
        WatchMenu(menu.Items);
        menu.Closed += (_, _) =>
        {
            shell.SuppressBlur = false;
            if (menuUsed)
            {
                Activate();
            }
            else
            {
                OnBlur();
            }

            menuUsed = false;
        };
    }

    void WatchMenu(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is not MenuItem entry)
            {
                continue;
            }

            if (entry.Items.Count == 0)
            {
                entry.Click += (_, _) => menuUsed = true;
            }
            else
            {
                WatchMenu(entry.Items);
            }
        }
    }

    void MoveNav(int delta)
    {
        var ids = new List<string> { "all" };
        ids.AddRange(S.Library.Groups.Where(group => group.Id != "fav").Select(group => group.Id));
        var index = ids.IndexOf(nav);
        if (index < 0)
        {
            index = 0;
        }

        var next = Math.Clamp(index + delta, 0, ids.Count - 1);
        if (ids[next] == nav)
        {
            return;
        }

        nav = ids[next];
        snapNav = true;
        Log.Info($"切换分组 {nav}");
        Render();
    }

    static FrameworkElement? FindChildByName(DependencyObject root, string name)
    {
        if (root is FrameworkElement element && element.Name == name)
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindChildByName(VisualTreeHelper.GetChild(root, i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    void OpenFirstShortcut()
    {
        var groups = nav == "all"
            ? S.Library.Groups.Where(group => group.Id != "fav")
            : S.Library.Groups.Where(group => group.Id == nav);
        foreach (var group in groups)
        {
            var item = ItemsFor(group).FirstOrDefault();
            if (item == null)
            {
                continue;
            }

            try
            {
                Session.Open(item.Path);
                S.TouchRecentFromPath(item.Path, item.Kind, item.Title);
                Dismiss();
            }
            catch (Exception ex)
            {
                Log.Warn($"打开失败 {ex.Message}");
            }

            return;
        }
    }

    public void Render()
    {
        if (nav == "fav")
        {
            nav = "all";
        }

        var savedContent = contentScroller?.VerticalOffset ?? 0;
        var savedNav = navScroller?.VerticalOffset ?? 0;
        var pinNav = snapNav;
        snapNav = false;
        rendering = true;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(S.Store.Layout("windows", "panelFooterHeight")) });

        var headerDock = new DockPanel { Margin = new Thickness(16, 0, 8, 0) };
        EnableDrag(headerDock);
        var close = PanelIconButton(Glyph.Close, Dismiss);
        var gear = PanelIconButton(Glyph.Gear, () => shell.ShowSettings());
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
        var editButton = edit
            ? C.Primary("✓ 完成", () => { edit = !edit; Render(); })
            : C.Soft("✎ 编辑", () => { edit = !edit; Render(); });
        var addTop = C.Primary(S.Store.Text("empty.panelAction"), AddShortcut);
        DockPanel.SetDock(editButton, Dock.Right);
        DockPanel.SetDock(addTop, Dock.Right);
        headerDock.Children.Add(editButton);
        headerDock.Children.Add(addTop);
        headerDock.Children.Add(BuildHeaderContext());
        var header = new Border
        {
            Background = C.Glass("bg.float", 0.72),
            BorderBrush = C.Brush("border.default"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = headerDock
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S.Store.Layout("windows", "navWidth")) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var navBody = new DockPanel { Margin = new Thickness(0, 12, 0, 8) };
        EnableDrag(navBody);
        var newGroup = C.Soft("新建分组", AddGroup);
        newGroup.Height = 34;
        newGroup.Margin = new Thickness(8, 8, 8, 0);
        newGroup.HorizontalAlignment = HorizontalAlignment.Stretch;
        DockPanel.SetDock(newGroup, Dock.Bottom);
        navBody.Children.Add(newGroup);
        var navList = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
        var navHead = C.Label("分组", 11, FontWeights.SemiBold, "text.tertiary");
        navHead.Margin = new Thickness(2, 0, 0, 8);
        navList.Children.Add(navHead);
        Nav(navList, "all", "全部", S.Library.Items.Count, null);
        foreach (var group in S.Library.Groups.Where(group => group.Id != "fav"))
        {
            Nav(navList, group.Id, group.Name, group.ItemIds.Count, group.Color);
        }

        var navScroll = new ScrollViewer
        {
            Content = navList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            AllowDrop = true
        };
        navScroll.DragOver += NavScrollDragOver;
        navScroll.Drop += NavScrollDrop;
        navScroller = navScroll;
        navBody.Children.Add(navScroll);
        var navPane = new Border
        {
            Background = C.Glass("bg.float", 0.55),
            BorderBrush = C.Brush("border.default"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = navBody
        };
        Grid.SetColumn(navPane, 0);
        body.Children.Add(navPane);
        var canvas = new StackPanel { Margin = new Thickness(8) };
        DrawGroups(canvas);
        var scroll = new ScrollViewer
        {
            Content = canvas,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent
        };
        Grid.SetColumn(scroll, 1);
        contentScroller = scroll;
        body.Children.Add(scroll);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footDock = new DockPanel { Margin = new Thickness(16, 0, 16, 0) };
        var tip = C.Label(
            S.Store.Text("panel.footer", new Dictionary<string, string>
            {
                ["hotkey"] = shell.PanelHotkeyText(),
                ["count"] = S.Library.Items.Count.ToString()
            }),
            12,
            FontWeights.Normal,
            "text.tertiary");
        tip.TextTrimming = TextTrimming.CharacterEllipsis;
        tip.VerticalAlignment = VerticalAlignment.Center;
        footDock.Children.Add(tip);
        EnableDrag(footDock);
        var foot = new Border
        {
            Background = C.Glass("bg.float", 0.72),
            BorderBrush = C.Brush("border.default"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footDock
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
        dragCaptionText = C.Label("", 12, FontWeights.SemiBold, "text.primary");
        dragCaption = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(8),
            Background = C.Brush("bg.float"),
            BorderBrush = C.Brush("accent"),
            BorderThickness = new Thickness(1),
            Child = dragCaptionText
        };
        Grid.SetRowSpan(dragCaption, 3);
        root.Children.Add(dragCaption);
        Content = C.Float(root, "bg.canvas");
        var contentTarget = contentScroller;
        var navTarget = navScroller;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (pinNav)
            {
                contentTarget?.ScrollToVerticalOffset(0);
                if (navTarget != null)
                {
                    FindChildByName(navTarget, nav)?.BringIntoView();
                }
            }
            else
            {
                contentTarget?.ScrollToVerticalOffset(savedContent);
                navTarget?.ScrollToVerticalOffset(savedNav);
            }
        });
        rendering = false;
        Log.Info($"打开快捷启动面板 {Width:0}x{Height:0}");
    }

    void Nav(Panel nav, string id, string title, int count, string? color)
    {
        var on = navId(id);
        var library = IsLibraryNavId(id);
        var row = new Border
        {
            Name = id,
            Height = 34,
            Margin = new Thickness(0, 4, 0, 0),
            CornerRadius = new CornerRadius(8),
            Background = on ? C.Brush("accent.soft") : Brushes.Transparent,
            Cursor = Cursors.Hand,
            AllowDrop = library
        };
        if (!on)
        {
            var hoverFill = HoverSoft();
            row.MouseEnter += (_, _) =>
            {
                if (navPressRow != row)
                {
                    row.Background = hoverFill;
                }
            };
            row.MouseLeave += (_, _) =>
            {
                if (navPressRow != row)
                {
                    row.Background = Brushes.Transparent;
                }
            };
        }
        var grid = new DockPanel { Margin = new Thickness(10, 0, 8, 0) };
        var number = C.Label(count.ToString(), 11, FontWeights.Normal, "text.tertiary");
        DockPanel.SetDock(number, Dock.Right);
        grid.Children.Add(number);
        if (color != null)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Chrome.Paint(color)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Left);
            grid.Children.Add(dot);
        }
        else
        {
            var align = new Border { Width = 14, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(align, Dock.Left);
            grid.Children.Add(align);
        }

        if (library && renaming == id)
        {
            var field = new TextBox
            {
                Text = title,
                Height = 28,
                MaxWidth = 140,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Background = C.Brush("bg.input"),
                Foreground = C.Brush("text.primary"),
                BorderBrush = C.Brush("border.strong"),
                Padding = new Thickness(6, 0, 6, 0)
            };
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

                CommitRename(id, field.Text);
                e.Handled = true;
            };
            field.LostFocus += (_, _) => CommitRename(id, field.Text);
            field.Loaded += (_, _) => field.Focus();
            grid.Children.Add(field);
        }
        else
        {
            var name = C.Label(title, 13, on ? FontWeights.SemiBold : FontWeights.Normal, on ? "accent.hover" : "text.secondary");
            grid.Children.Add(name);
        }

        row.Tag = "nav";
        row.Child = grid;
        if (library)
        {
            var menu = new ContextMenu();
            var renameItem = new MenuItem { Header = S.Store.Text("group.rename") };
            renameItem.Click += (_, _) => { renaming = id; Render(); };
            menu.Items.Add(renameItem);
            if (CanDeleteGroup(id))
            {
                var deleteItem = new MenuItem { Header = S.Store.Text("group.delete") };
                deleteItem.Click += (_, _) => ConfirmDeleteGroup(id);
                menu.Items.Add(deleteItem);
            }

            KeepOpenWhileMenu(menu);
            row.ContextMenu = menu;
            row.DragOver += NavDragOver;
            row.Drop += NavRowDrop;
            row.DragLeave += (_, _) => ClearNavDropHint();
            row.PreviewMouseLeftButtonDown += (_, e) => BeginNavPress(row, id, e);
            row.PreviewMouseMove += (_, e) => NavPressMove(row, e);
            row.PreviewMouseLeftButtonUp += (_, e) => EndNavPress(row, id, e);
        }
        else
        {
            row.MouseLeftButtonUp += (_, _) => { this.nav = id; Render(); };
        }

        nav.Children.Add(row);
    }

    static bool IsLibraryNavId(string id) => id is not "all";

    void BeginNavPress(Border row, string id, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox || renaming == id)
        {
            return;
        }

        navPressRow = row;
        navPressId = id;
        navPressPoint = e.GetPosition(row);
        navReorderArmed = false;
        navReorderTarget = null;
        row.CaptureMouse();
        navArmTimer?.Stop();
        navArmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        navArmTimer.Tick += (_, _) =>
        {
            navArmTimer?.Stop();
            if (navPressId == id && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                ArmNavReorder(row);
            }
        };
        navArmTimer.Start();
        e.Handled = true;
    }

    void NavPressMove(Border row, System.Windows.Input.MouseEventArgs e)
    {
        if (navPressId == null || navPressRow != row || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var moved = e.GetPosition(row) - navPressPoint;
        if (!navReorderArmed && moved.Length > 10)
        {
            ArmNavReorder(row);
        }

        if (!navReorderArmed || navScroller == null)
        {
            return;
        }

        var y = e.GetPosition(navScroller).Y;
        if (y < 28)
        {
            navScroller.ScrollToVerticalOffset(navScroller.VerticalOffset - 16);
        }
        else if (y > navScroller.ActualHeight - 28)
        {
            navScroller.ScrollToVerticalOffset(navScroller.VerticalOffset + 16);
        }

        if (TryFindNearestNavRow(navScroller, e.GetPosition(navScroller), out var target, out var after) && target.Name != navPressId)
        {
            navReorderTarget = target.Name;
            navInsertAfter = after;
            ShowNavInsertHint(target, after);
        }

        e.Handled = true;
    }

    void EndNavPress(Border row, string id, MouseButtonEventArgs e)
    {
        if (navPressId != id)
        {
            return;
        }

        var armed = navReorderArmed;
        var target = navReorderTarget;
        var after = navInsertAfter;
        ResetNavPress(row);
        if (armed && target != null && target != id)
        {
            S.ReorderGroup(id, target, after);
        }
        else
        {
            nav = id;
            Render();
        }

        e.Handled = true;
    }

    void ArmNavReorder(Border row)
    {
        if (navReorderArmed)
        {
            return;
        }

        navReorderArmed = true;
        navArmTimer?.Stop();
        row.Opacity = 0.55;
        row.Cursor = Cursors.SizeAll;
    }

    void ResetNavPress(Border row)
    {
        navArmTimer?.Stop();
        navPressId = null;
        navPressRow = null;
        navReorderArmed = false;
        navReorderTarget = null;
        row.Opacity = 1;
        row.Cursor = Cursors.Hand;
        row.ReleaseMouseCapture();
        ClearNavDropHint();
    }

    bool TryFindNearestNavRow(ScrollViewer scroll, Point point, out Border row, out bool after)
    {
        row = null!;
        after = false;
        if (scroll.Content is not StackPanel list)
        {
            return false;
        }

        Border? nearest = null;
        var best = double.MaxValue;
        foreach (var child in list.Children.OfType<Border>())
        {
            if (!IsLibraryNavId(child.Name))
            {
                continue;
            }

            var topLeft = child.TranslatePoint(new Point(0, 0), scroll);
            var mid = topLeft.Y + child.ActualHeight / 2;
            var distance = Math.Abs(point.Y - mid);
            if (distance < best)
            {
                best = distance;
                nearest = child;
                after = point.Y > mid;
            }
        }

        if (nearest == null)
        {
            return false;
        }

        row = nearest;
        return true;
    }

    void NavRowDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border row || !IsLibraryNavId(row.Name))
        {
            return;
        }

        var y = e.GetPosition(row).Y;
        ApplyNavReorder(e, row.Name, y > row.ActualHeight / 2);
        e.Handled = true;
    }

    void NavDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("deskkit-shortcut"))
        {
            return;
        }

        if (!e.Data.GetDataPresent("deskkit-group"))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        if (sender is not Border row || !IsLibraryNavId(row.Name))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        var y = e.GetPosition(row).Y;
        ShowNavInsertHint(row, y > row.ActualHeight / 2);
    }

    void NavScrollDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("deskkit-shortcut"))
        {
            return;
        }

        if (!e.Data.GetDataPresent("deskkit-group"))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        if (sender is not ScrollViewer scroll)
        {
            return;
        }

        var point = e.GetPosition(scroll);
        if (TryFindNavRowAt(scroll, point, out var row, out var after))
        {
            ShowNavInsertHint(row, after);
        }
        else
        {
            ClearNavDropHint();
        }
    }

    void NavScrollDrop(object sender, DragEventArgs e)
    {
        if (sender is not ScrollViewer scroll)
        {
            return;
        }

        var point = e.GetPosition(scroll);
        if (!TryFindNavRowAt(scroll, point, out var row, out var after))
        {
            return;
        }

        ApplyNavReorder(e, row.Name, after);
        e.Handled = true;
    }

    bool TryFindNavRowAt(ScrollViewer scroll, Point point, out Border row, out bool after)
    {
        row = null!;
        after = false;
        if (scroll.Content is not StackPanel list)
        {
            return false;
        }

        foreach (var child in list.Children.OfType<Border>())
        {
            if (!IsLibraryNavId(child.Name))
            {
                continue;
            }

            var topLeft = child.TranslatePoint(new Point(0, 0), scroll);
            var bottom = topLeft.Y + child.ActualHeight;
            if (point.Y < topLeft.Y || point.Y > bottom)
            {
                continue;
            }

            row = child;
            after = point.Y > topLeft.Y + child.ActualHeight / 2;
            return true;
        }

        return false;
    }

    void ApplyNavReorder(DragEventArgs e, string targetId, bool placeAfter)
    {
        if (e.Data.GetData("deskkit-group") is not string source || !IsLibraryNavId(source) || !IsLibraryNavId(targetId) || source == targetId)
        {
            return;
        }

        S.ReorderGroup(source, targetId, placeAfter);
        ClearNavDropHint();
        Render();
    }

    void ShowNavInsertHint(Border row, bool after)
    {
        if (navDropHint == row && navInsertAfter == after)
        {
            return;
        }

        ClearNavDropHint();
        navDropHint = row;
        navInsertAfter = after;
        row.BorderBrush = C.Brush("accent");
        row.BorderThickness = after ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
        if (!navId(row.Name))
        {
            row.Background = C.Brush("bg.hover");
        }
    }

    void ClearNavDropHint()
    {
        if (navDropHint == null)
        {
            return;
        }

        var id = navDropHint.Name;
        navDropHint.BorderThickness = new Thickness(0);
        navDropHint.BorderBrush = C.Brush("border.default");
        navDropHint.Background = navId(id) ? C.Brush("accent.soft") : Brushes.Transparent;
        navDropHint = null;
    }

    bool navId(string id) => nav == id;

    UIElement BuildHeaderContext()
    {
        var ctx = NavContext();
        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var brand = C.Label("DeskKit", 13, FontWeights.SemiBold, "text.tertiary");
        DockPanel.SetDock(brand, Dock.Left);
        row.Children.Add(brand);
        var inner = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (ctx.Color != null)
        {
            inner.Children.Add(new Border
            {
                Width = 3,
                Height = 14,
                Background = new SolidColorBrush(Chrome.Paint(ctx.Color)),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var title = C.Label(ctx.Title, 16, FontWeights.SemiBold, "text.primary");
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.VerticalAlignment = VerticalAlignment.Center;
        inner.Children.Add(title);
        var meta = C.Label($"· {ctx.Count} 项", 12, FontWeights.Normal, "text.tertiary");
        meta.Margin = new Thickness(8, 0, 0, 0);
        meta.VerticalAlignment = VerticalAlignment.Center;
        inner.Children.Add(meta);
        row.Children.Add(inner);
        return row;
    }

    Button PanelIconButton(string glyph, Action click)
    {
        var button = C.IconButton(glyph, click);
        button.Width = 32;
        button.Height = 32;
        return button;
    }

    TextBlock PanelHintLabel()
    {
        var hint = new TextBlock
        {
            FontSize = 12,
            Foreground = C.Brush("text.tertiary"),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        hint.Inlines.Add(new Run(S.Store.Text("empty.panelHintBefore")));
        hint.Inlines.Add(new Run(S.Store.Text("empty.panelHintAction")) { Foreground = C.Brush("accent.hover") });
        return hint;
    }

    FrameworkElement DashedDropZone(UIElement content)
    {
        var host = new Grid
        {
            MinWidth = 280,
            MaxWidth = 420,
            Margin = new Thickness(0, 0, 0, 20)
        };
        host.Children.Add(new System.Windows.Shapes.Rectangle
        {
            RadiusX = 10,
            RadiusY = 10,
            Stroke = C.Brush("border.strong"),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = C.Glass("bg.float", 0.4)
        });
        var pad = new Border { Padding = new Thickness(24, 22, 24, 22), Background = Brushes.Transparent };
        pad.Child = content;
        host.Children.Add(pad);
        return host;
    }

    FrameworkElement DashedCellFrame(double width, double height)
    {
        var grid = new Grid { Width = width, Height = height };
        grid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            RadiusX = 8,
            RadiusY = 8,
            Stroke = C.Brush("border.strong"),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = Brushes.Transparent
        });
        return grid;
    }

    (string Title, int Count, string? Color) NavContext() => nav switch
    {
        "all" => ("全部", S.Library.Items.Count, null),
        _ => S.Library.Groups.FirstOrDefault(group => group.Id == nav) is { } group
            ? (group.Name, group.ItemIds.Count, group.Color)
            : ("全部", S.Library.Items.Count, null)
    };

    List<Shortcut> ItemsFor(Group group) => group.ItemIds
        .Select(id => S.Library.Items.FirstOrDefault(item => item.Id == id))
        .Where(item => item != null)
        .Cast<Shortcut>()
        .Where(item => search.Length == 0 || item.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
        .ToList();

    void AppendFocusEmpty(StackPanel stack)
    {
        var drop = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var glyph = C.Label("↓", 28, FontWeights.Normal, "text.tertiary");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        drop.Children.Add(glyph);
        var sub = C.Label(S.Store.Text("empty.panelDrop"), 12, FontWeights.Normal, "text.tertiary");
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.Margin = new Thickness(0, 8, 0, 0);
        drop.Children.Add(sub);
        stack.Children.Add(DashedDropZone(drop));
        var empty = C.Label(S.Store.Text("empty.panel"), 13, FontWeights.Normal, "text.secondary");
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.TextAlignment = TextAlignment.Center;
        empty.MaxWidth = 360;
        empty.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(empty);
        stack.Children.Add(PanelHintLabel());
    }

    UIElement ItemGrid(IReadOnlyList<Shortcut> items, string groupId)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0) };
        foreach (var item in items)
        {
            wrap.Children.Add(Cell(item, groupId));
        }

        if (edit)
        {
            var add = new Grid { Width = 80, Height = 80, Margin = new Thickness(4), Cursor = Cursors.Hand };
            add.Children.Add(DashedCellFrame(80, 80));
            var plus = C.Label("添加", 12, FontWeights.Normal, "text.tertiary");
            plus.HorizontalAlignment = HorizontalAlignment.Center;
            plus.VerticalAlignment = VerticalAlignment.Center;
            add.Children.Add(plus);
            add.MouseLeftButtonUp += (_, _) => AddShortcut();
            wrap.Children.Add(add);
        }

        return new Border { Padding = new Thickness(8, 8, 8, 16), Child = wrap };
    }

    void DrawGroups(Panel canvas)
    {
        if (nav == "all")
        {
            foreach (var group in S.Library.Groups.Where(group => group.Id != "fav"))
            {
                DrawGroupCard(canvas, group);
            }

            return;
        }

        foreach (var group in S.Library.Groups.Where(group => group.Id == nav))
        {
            DrawFocusGroup(canvas, group);
        }
    }

    void DrawFocusGroup(Panel canvas, Group group)
    {
        var items = ItemsFor(group);
        if (items.Count == 0 && !edit)
        {
            var host = new Grid { MinHeight = 420, Margin = new Thickness(0) };
            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(32)
            };
            AppendFocusEmpty(stack);
            host.Children.Add(stack);
            canvas.Children.Add(host);
            return;
        }

        canvas.Children.Add(ItemGrid(items, group.Id));
    }

    void DrawGroupCard(Panel canvas, Group group)
    {
        var items = ItemsFor(group);
        var isFolded = folded.Contains(group.Id);
        var card = new Border
        {
            Background = C.Glass("bg.float", 0.7),
            BorderBrush = C.Brush("border.default"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(8)
        };
        var stack = new StackPanel();
        var head = new DockPanel();
        var fold = C.IconButton(isFolded ? Glyph.Right : Glyph.Down, () =>
        {
            if (!folded.Add(group.Id)) folded.Remove(group.Id);
            Render();
        });
        DockPanel.SetDock(fold, Dock.Right);
        head.Children.Add(fold);

        var bar = new Border { Width = 3, Height = 14, Background = new SolidColorBrush(Chrome.Paint(group.Color)), CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(bar, Dock.Left);
        head.Children.Add(bar);
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

        stack.Children.Add(head);
        if (!isFolded)
        {
            if (items.Count == 0 && !edit)
            {
                var empty = C.Label(S.Store.Text("empty.panelStack"), 13, FontWeights.Normal, "text.tertiary");
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.TextAlignment = TextAlignment.Center;
                empty.Margin = new Thickness(0, 24, 0, 24);
                stack.Children.Add(empty);
            }
            else
            {
                var wrap = ItemGrid(items, group.Id);
                if (wrap is FrameworkElement gridBlock)
                {
                    gridBlock.Margin = new Thickness(0, 12, 0, 0);
                }

                stack.Children.Add(wrap);
            }
        }

        card.Tag = group.Id;
        card.Child = stack;
        canvas.Children.Add(card);
    }

    UIElement Cell(Shortcut item, string sourceGroup)
    {
        var cell = new Grid { Width = 80, Height = 80, Margin = new Thickness(4), Cursor = Cursors.Hand };
        var icon = ShortcutIcon(item);
        var label = C.Label(item.Title, 11, FontWeights.Normal, "text.secondary");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Margin = new Thickness(2, 0, 2, 6);
        label.TextAlignment = TextAlignment.Center;
        cell.Children.Add(icon);
        cell.Children.Add(label);
        cell.Tag = "cell|" + sourceGroup + "|" + item.Id;
        cell.Background = Brushes.Transparent;
        var hoverFill = C.Brush("bg.hover");
        cell.MouseEnter += (_, _) => cell.Background = hoverFill;
        cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
        if (edit)
        {
            Wobble(cell);
        }
        if (edit)
        {
            var del = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(4),
                Background = C.Brush("bg.float"),
                BorderBrush = C.Brush("border.strong"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2, 2, 0, 0),
                Cursor = Cursors.Hand,
                Child = C.Label("−", 14, FontWeights.Normal, "text.secondary"),
                Tag = "cell-delete"
            };
            del.MouseLeftButtonUp += (_, e) =>
            {
                RemoveShortcut(item.Id);
                e.Handled = true;
            };
            cell.Children.Add(del);
        }

        var menu = new ContextMenu();
        var move = new MenuItem { Header = S.Store.Text("shortcut.move") };
        foreach (var group in S.Library.Groups.Where(entry => entry.Id != "fav" && entry.Id != sourceGroup))
        {
            var dest = group;
            var choice = new MenuItem { Header = dest.Name };
            choice.Click += (_, _) => S.MoveShortcut(item.Id, sourceGroup, dest.Id);
            move.Items.Add(choice);
        }

        if (move.Items.Count == 0)
        {
            move.Items.Add(new MenuItem { Header = S.Store.Text("shortcut.moveEmpty"), IsEnabled = false });
        }

        menu.Items.Add(move);
        var reveal = new MenuItem
        {
            Header = S.Store.Text("shortcut.reveal"),
            IsEnabled = item.Kind != "url" && !string.IsNullOrWhiteSpace(item.Path)
        };
        reveal.Click += (_, _) =>
        {
            try
            {
                Session.Reveal(item.Path);
            }
            catch (Exception ex)
            {
                Log.Warn($"打开所在文件夹失败 {ex.Message}");
            }
        };
        menu.Items.Add(reveal);
        var remove = new MenuItem { Header = S.Store.Text("shortcut.delete") };
        remove.Click += (_, _) => RemoveShortcut(item.Id);
        menu.Items.Add(remove);
        KeepOpenWhileMenu(menu);
        cell.ContextMenu = menu;

        var pressedAt = DateTime.UtcNow;
        var dragging = false;
        cell.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsCellDelete(e.OriginalSource as DependencyObject))
            {
                return;
            }

            pressedAt = DateTime.UtcNow;
            dragging = false;
        };
        cell.PreviewMouseMove += (_, e) =>
        {
            if (dragging || e.LeftButton != MouseButtonState.Pressed || IsCellDelete(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if ((DateTime.UtcNow - pressedAt).TotalMilliseconds < 250)
            {
                return;
            }

            dragging = true;
            Lift(cell);
            var data = new System.Windows.DataObject("deskkit-shortcut", item.Id + "\n" + sourceGroup);
            try
            {
                DragDrop.DoDragDrop(cell, data, DragDropEffects.Move);
            }
            finally
            {
                Settle(cell);
                HideDragCaption();
                HideDropHint();
            }

            e.Handled = true;
        };
        cell.GiveFeedback += (_, feedback) =>
        {
            feedback.UseDefaultCursors = false;
            Mouse.SetCursor(Cursors.Hand);
            feedback.Handled = true;
        };

        cell.MouseLeftButtonUp += (_, e) =>
        {
            if (dragging || edit) return;
            try
            {
                Session.Open(item.Path);
                S.TouchRecentFromPath(item.Path, item.Kind, item.Title);
                Dismiss();
            }
            catch (Exception ex) { Log.Warn($"打开失败 {ex.Message}"); }
            e.Handled = true;
        };
        return cell;
    }

    void RemoveShortcut(string id)
    {
        S.Library.Items.RemoveAll(entry => entry.Id == id);
        foreach (var group in S.Library.Groups)
        {
            group.ItemIds.RemoveAll(itemId => itemId == id);
        }

        S.Save();
        Render();
    }

    static bool IsCellDelete(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement element && element.Tag as string == "cell-delete")
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    static bool ShortcutDrag(DragEventArgs e, out string itemId, out string sourceGroup)
    {
        itemId = "";
        sourceGroup = "";
        if (e.Data.GetData("deskkit-shortcut") is not string payload)
        {
            return false;
        }

        var parts = payload.Split('\n');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        itemId = parts[0];
        sourceGroup = parts[1];
        return true;
    }

    void CommitRename(string id, string name)
    {
        if (renaming != id) return;
        renaming = null;
        S.RenameGroup(id, name);
        Render();
    }

    static bool CanDeleteGroup(string id) => id is not ("all" or "ungrouped");

    void ConfirmDeleteGroup(string groupId)
    {
        if (!CanDeleteGroup(groupId))
        {
            return;
        }

        var group = S.Library.Groups.FirstOrDefault(entry => entry.Id == groupId);
        if (group == null)
        {
            return;
        }

        var body = S.Store.Text("group.deleteConfirm", new Dictionary<string, string>
        {
            ["name"] = group.Name,
            ["count"] = group.ItemIds.Count.ToString()
        });
        shell.SuppressBlur = true;
        var answer = MessageBox.Show(body, S.Store.Text("group.deleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        shell.SuppressBlur = false;
        Activate();
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        S.DeleteGroup(groupId);
        if (nav == groupId)
        {
            nav = "all";
        }

        renaming = null;
        edit = false;
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
        renaming = id;
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
        if (ShortcutDrag(e, out var movingId, out var from))
        {
            e.Handled = true;
            HideDropHint();
            if (edit && CellUnder(e) is { } cell && cell.Group == from && cell.Item != movingId)
            {
                e.Effects = DragDropEffects.Move;
                ShowDragCaption(S.Store.Text(cell.After ? "shortcut.dropAfter" : "shortcut.dropBefore"), e);
                return;
            }

            var hover = GroupUnder(e);
            var canMove = hover != null && hover != from;
            e.Effects = canMove ? DragDropEffects.Move : DragDropEffects.None;
            if (canMove)
            {
                var name = S.Library.Groups.FirstOrDefault(group => group.Id == hover)?.Name ?? hover!;
                ShowDragCaption(S.Store.Text("shortcut.dropMove", new Dictionary<string, string> { ["name"] = name }), e);
                HighlightGroup(hover);
            }
            else
            {
                HideDragCaption();
                HighlightGroup(null);
            }

            return;
        }

        var allowed = CanDrop(e);
        externalDragOver = allowed;
        if (allowed)
        {
            externalDragTick = Environment.TickCount;
        }
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
        if (ShortcutDrag(e, out var movingId, out var from))
        {
            HideDropHint();
            HideDragCaption();
            if (edit && CellUnder(e) is { } cell && cell.Group == from && cell.Item != movingId)
            {
                S.ReorderShortcut(movingId, from, cell.Item, cell.After);
            }
            else
            {
                var hover = GroupUnder(e);
                if (hover != null && hover != from)
                {
                    S.MoveShortcut(movingId, from, hover);
                }
            }

            e.Handled = true;
            return;
        }

        // 松手添加成功后窗口要留着。否则这次失焦的延时隐藏会把刚打开的面板收掉。
        externalDragOver = false;
        blurGeneration++;
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
        Activate();
    }

    UIElement ShortcutIcon(Shortcut item)
    {
        if (item.Kind == "url" || string.IsNullOrWhiteSpace(item.Path))
        {
            return ShortcutGlyph(item.Kind == "url" ? Glyph.Link : Glyph.App);
        }

        // 第二个参数是要更新的控件。不传的话只会留下「所有 exe 共用」的那张通用图。
        var image = new Image
        {
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 0, 0)
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.Source = Icons.File(item.Path, 40, image);
        return image.Source != null ? image : ShortcutGlyph(Glyph.App);
    }

    TextBlock ShortcutGlyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = Chrome.IconFont,
        FontSize = 22,
        Foreground = C.Brush("accent"),
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 10, 0, 0)
    };

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
            if (hit is FrameworkElement element)
            {
                if (element.Tag is string tag && S.Library.Groups.Any(group => group.Id == tag))
                {
                    return tag;
                }

                if (S.Library.Groups.Any(group => group.Id == element.Name))
                {
                    return element.Name;
                }
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }

    string DropTarget(string? hover)
    {
        if (hover != null && S.Library.Groups.Any(group => group.Id == hover))
        {
            return hover;
        }

        if (nav is not "all" && S.Library.Groups.Any(group => group.Id == nav))
        {
            return nav;
        }

        if (S.Library.Groups.Any(group => group.Id == "ungrouped"))
        {
            return "ungrouped";
        }

        return S.Library.Groups.FirstOrDefault()?.Id ?? "ungrouped";
    }

    (string Group, string Item, bool After)? CellUnder(DragEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(this, e.GetPosition(this))?.VisualHit as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement element && element.Tag is string tag && tag.StartsWith("cell|", StringComparison.Ordinal))
            {
                var parts = tag.Split('|', 3);
                if (parts.Length == 3)
                {
                    var y = e.GetPosition(element).Y;
                    return (parts[1], parts[2], y > element.ActualHeight / 2);
                }
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }

    void ShowDragCaption(string text, DragEventArgs e)
    {
        if (dragCaption == null || dragCaptionText == null)
        {
            return;
        }

        var point = e.GetPosition(this);
        dragCaptionText.Text = text;
        dragCaption.Margin = new Thickness(point.X + 16, point.Y + 18, 0, 0);
        dragCaption.Visibility = Visibility.Visible;
    }

    void HideDragCaption()
    {
        if (dragCaption != null)
        {
            dragCaption.Visibility = Visibility.Collapsed;
        }
    }

    SolidColorBrush HoverSoft()
    {
        var color = C.Brush("accent.soft").Color;
        var brush = new SolidColorBrush(Color.FromArgb(115, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    static void Wobble(FrameworkElement cell)
    {
        var rotate = new RotateTransform();
        var scale = new ScaleTransform(1, 1);
        cell.RenderTransformOrigin = new Point(0.5, 0.5);
        cell.RenderTransform = new TransformGroup { Children = { scale, rotate } };
        var animation = new DoubleAnimation(-0.86, 0.86, TimeSpan.FromSeconds(0.14))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(6),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    static void Lift(FrameworkElement cell)
    {
        if (cell.RenderTransform is not TransformGroup group)
        {
            var scale = new ScaleTransform(1.05, 1.05);
            cell.RenderTransformOrigin = new Point(0.5, 0.5);
            cell.RenderTransform = scale;
        }
        else
        {
            foreach (var child in group.Children)
            {
                if (child is ScaleTransform scale)
                {
                    scale.ScaleX = 1.05;
                    scale.ScaleY = 1.05;
                }
            }
        }

        cell.Opacity = 0.92;
    }

    static void Settle(FrameworkElement cell)
    {
        cell.Opacity = 1;
        if (cell.RenderTransform is ScaleTransform only)
        {
            only.ScaleX = 1;
            only.ScaleY = 1;
            return;
        }

        if (cell.RenderTransform is not TransformGroup group)
        {
            return;
        }

        foreach (var child in group.Children)
        {
            if (child is ScaleTransform scale)
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        }
    }

    void ShowDropHint(string groupId, bool moving = false)
    {
        if (dropVeil == null || dropTitle == null)
        {
            return;
        }

        var name = S.Library.Groups.FirstOrDefault(group => group.Id == groupId)?.Name ?? "未分组";
        dropTitle.Text = moving ? $"松手移动到「{name}」" : $"松手添加到「{name}」";
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
