using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DeskKit.Platform;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;

namespace DeskKit.App;

public static class Glyph
{
    public const string Search = "\uE721";
    public const string Gear = "\uE713";
    public const string Close = "\uE711";
    public const string Back = "\uE72B";
    public const string Down = "\uE70D";
    public const string Right = "\uE76C";
    public const string Plus = "\uE710";
    public const string Clipboard = "\uE8C8";
    public const string Picker = "\uE790";
    public const string Translate = "\uE8C1";
    public const string Calc = "\uE8EF";
    public const string Clock = "\uE823";
    public const string Swap = "\uE8AB";
    public const string Paint = "\uE70F";
    public const string Link = "\uE71B";
    public const string Doc = "\uE8A5";
    public const string App = "\uECAA";
    public const string Edit = "\uE70F";
    public const string Delete = "\uE74D";

    public static string ForTool(string? id) => id switch
    {
        "clipboard" => Clipboard,
        "picker" => Picker,
        "translate" => Translate,
        "calc" => Calc,
        "timestamp" => Clock,
        "unit" => Swap,
        "color" => Paint,
        "qr" => App,
        _ => App
    };
}

/// <summary>把 contracts 里的色值和尺寸变成 WPF 控件。界面不要自己写色号。</summary>
public sealed class Chrome
{
    static ControlTemplate? flat;
    public Session Session { get; }
    readonly Dictionary<string, SolidColorBrush> cache = new(StringComparer.Ordinal);
    readonly Dictionary<Window, int> fadeTokens = new();

    public Chrome(Session session) => Session = session;

    public static FontFamily Font { get; } = new("Microsoft YaHei UI, Segoe UI");
    public static FontFamily IconFont { get; } = new("Segoe MDL2 Assets");

    public bool Dark => Session.Settings.Theme switch
    {
        "dark" => true,
        "system" => !AppsUseLightTheme(),
        _ => false
    };

    public void Invalidate()
    {
        cache.Clear();
        painted = "";
        paintedBrush = null;
    }

    public SolidColorBrush Brush(string name)
    {
        var key = name == "accent" && Session.Settings.AccentToken != "accent" ? Session.Settings.AccentToken : name;
        var hex = Session.Store.Color(key, Dark);
        var id = (Dark ? "d:" : "l:") + key + hex;
        if (cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush(Paint(hex));
        brush.Freeze();
        cache[id] = brush;
        return brush;
    }

    public static MediaColor Paint(string hex)
    {
        var text = hex.StartsWith('#') ? hex : "#" + hex;
        return (MediaColor)ColorConverter.ConvertFromString(text)!;
    }

    public TextBlock Label(string text, double size, FontWeight weight, string color) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brush(color),
        FontFamily = Font,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };

    public Button Soft(string text, Action click) => Button(text, click, primary: false);

    public Button Primary(string text, Action click) => Button(text, click, primary: true);

    public Button IconButton(string glyph, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14, Foreground = Brush("text.secondary"), HorizontalAlignment = HorizontalAlignment.Center },
            Width = 28,
            Height = 28,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Flat(),
            Focusable = false
        };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>与 mac <c>magnifyingglass</c> 一致的描边放大镜，尺寸对齐 AppIcons.symbol。</summary>
    public FrameworkElement SearchMark(double size)
    {
        const double view = 16;
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M7,3.25 A4,4 0 1,1 7,11.25 A4,4 0 1,1 7,3.25 M10.1,10.1 L13.25,13.25"),
            Stroke = Brush("text.tertiary"),
            StrokeThickness = 1.5 * (size / view),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return path;
    }

    public Border Keycap(string text) => new()
    {
        Background = Brush("bg.input"),
        BorderBrush = Brush("border.default"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(6, 1, 6, 1),
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Label(text, 11, FontWeights.SemiBold, "text.secondary")
    };

    public UIElement Switch(bool on, Action<bool> changed)
    {
        var knob = new System.Windows.Shapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = Brushes.White,
            HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center
        };
        var track = new Border
        {
            Width = 40,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = on ? Brush("accent") : Brush("border.strong"),
            Child = knob,
            Cursor = Cursors.Hand
        };
        track.MouseLeftButtonUp += (_, _) =>
        {
            on = !on;
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            track.Background = on ? Brush("accent") : Brush("border.strong");
            changed(on);
        };
        return track;
    }

    public ComboBox Menu(IReadOnlyList<(string Value, string Label)> options, string current, Action<string> pick)
    {
        var box = new ComboBox { Height = 28, MinWidth = 140, VerticalAlignment = VerticalAlignment.Center };
        foreach (var option in options)
        {
            box.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        }

        box.SelectedIndex = Math.Max(0, options.ToList().FindIndex(option => option.Value == current));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem item && item.Tag is string value)
            {
                pick(value);
            }
        };
        return box;
    }

    public StackPanel Chips(IReadOnlyList<(string Value, string Label)> options, string current, Action<string> pick)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var option in options)
        {
            var selected = option.Value == current;
            var button = new Button
            {
                Content = option.Label,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(10, 0, 10, 0),
                Background = selected ? Brush("accent.soft") : Brush("bg.subtle"),
                BorderBrush = selected ? Brush("accent") : Brush("border.default"),
                BorderThickness = new Thickness(1),
                Foreground = selected ? Brush("accent.hover") : Brush("text.secondary"),
                FontFamily = Font,
                Cursor = Cursors.Hand,
                Template = Flat()
            };
            var value = option.Value;
            button.Click += (_, _) => pick(value);
            panel.Children.Add(button);
        }

        return panel;
    }

    public Border Float(UIElement child, string fill)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(Session.Store.Layout("radius", "float")),
            BorderBrush = Brush("border.default"),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = child
        };
        var ink = Paint(Session.Store.Color("shadow.ink", Dark));
        border.Effect = new DropShadowEffect
        {
            Color = ink,
            BlurRadius = Math.Max(8, Session.Store.Layout("shadow", "blur1")),
            ShadowDepth = Session.Store.Layout("shadow", "y1"),
            Opacity = Session.Store.Layout("shadow", "alpha1")
        };
        PaintBackground(border, fill);
        return border;
    }

    string painted = "";
    Brush? paintedBrush;

    public void PaintBackground(Border border, string fill)
    {
        var alpha = Math.Min(1, Math.Max(0.15, Session.Settings.BackgroundOpacity));
        var path = Session.Settings.BackgroundImagePath;
        var revision = string.IsNullOrEmpty(path) || !File.Exists(path)
            ? 0
            : File.GetLastWriteTimeUtc(path).ToFileTimeUtc();
        var stamp = fill + "|" + path + "|" + revision + "|" + alpha.ToString("0.00") + "|" + Dark;
        if (painted == stamp && paintedBrush != null)
        {
            border.Background = paintedBrush;
            return;
        }
        Brush brush;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                var image = LoadBackgroundImage(path);
                brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill, Opacity = alpha };
            }
            catch (Exception ex)
            {
                Log.Warn($"背景图无法读取 {ex.Message}");
                brush = Flat(fill, alpha);
            }
        }
        else
        {
            brush = Flat(fill, alpha);
        }

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        painted = stamp;
        paintedBrush = brush;
        border.Background = brush;
    }

    SolidColorBrush Flat(string fill, double alpha)
    {
        var color = Brush(fill).Color;
        return new SolidColorBrush(MediaColor.FromArgb((byte)(alpha * 255), color.R, color.G, color.B));
    }

    static BitmapImage LoadBackgroundImage(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>叠在底图上的半透明色层。mac 快捷启动分组卡片约 42%。</summary>
    public SolidColorBrush Glass(string token, double layerAlpha)
    {
        var backdrop = Math.Min(1, Math.Max(0.15, Session.Settings.BackgroundOpacity));
        var color = Brush(token).Color;
        var a = (byte)Math.Clamp(layerAlpha * backdrop * 255, 24, 240);
        var brush = new SolidColorBrush(MediaColor.FromArgb(a, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public Border Card(string title, IReadOnlyList<(string Title, string Detail, UIElement? Control)> rows)
    {
        var stack = new StackPanel();
        if (title.Length > 0)
        {
            var heading = Label(title, 14, FontWeights.SemiBold, "text.primary");
            heading.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(heading);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(Label(row.Title, 13, FontWeights.Medium, "text.primary"));
            if (row.Detail.Length > 0)
            {
                var detail = Label(row.Detail, 11, FontWeights.Normal, "text.tertiary");
                detail.TextWrapping = TextWrapping.Wrap;
                detail.Margin = new Thickness(0, 2, 12, 0);
                text.Children.Add(detail);
            }

            Grid.SetColumn(text, 0);
            grid.Children.Add(text);
            if (row.Control is FrameworkElement control)
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 1);
                grid.Children.Add(control);
            }

            stack.Children.Add(grid);
            if (i < rows.Count - 1)
            {
                stack.Children.Add(new Border { Height = 1, Background = Brush("border.default") });
            }
        }

        return new Border
        {
            Background = Brush("bg.float"),
            BorderBrush = Brush("border.default"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14, 16, 8),
            Margin = new Thickness(16, 8, 16, 8),
            Child = stack
        };
    }

    public void Prepare(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.ShowInTaskbar = true;
        window.Topmost = true;
        window.ResizeMode = ResizeMode.NoResize;
        window.SizeToContent = SizeToContent.Manual;
        window.FontFamily = Font;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        var icon = AppIcon.WindowIcon();
        if (icon != null)
        {
            window.Icon = icon;
        }

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            NativeMethods.MakeToolWindow(hwnd);
        };
    }

    /// <summary>出现 120ms、消失 150ms。后一次动画会使前一次的收尾失效。</summary>
    public void FadeIn(Window window)
    {
        var token = NextFade(window);
        window.BeginAnimation(UIElement.OpacityProperty, null);
        window.Opacity = 0;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Motion("show")))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            if (!SameFade(window, token)) return;
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 1;
        };
        window.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public void FadeOut(Window window, Action done)
    {
        var token = NextFade(window);
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(Motion("blurHide")))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            if (!SameFade(window, token)) return;
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 0;
            done();
            window.Opacity = 1;
        };
        window.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    int NextFade(Window window)
    {
        fadeTokens.TryGetValue(window, out var token);
        token++;
        fadeTokens[window] = token;
        return token;
    }

    bool SameFade(Window window, int token) => fadeTokens.TryGetValue(window, out var current) && current == token;

    double Motion(string key)
    {
        var ms = Session.Store.Layout("motionMs", key);
        return ms > 0 ? ms : key == "blurHide" ? 150 : 120;
    }

    public void Present(Window window, double topRatio)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
        Place(window, topRatio);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.ForceForeground(hwnd);
        }
    }

    /// <summary>启动器用 Win32 像素框定位，避免 WPF 改高后顶边漂移。</summary>
    public void PresentLauncher(Window window)
    {
        var width = Session.Store.Layout("launcher", "width");
        var height = Session.Store.Layout("launcher", "defaultHeight");
        window.Width = width;
        window.Height = height;
        Log.Info($"启动器契约尺寸 {width:0}x{height:0}");
        if (!window.IsVisible)
        {
            window.Show();
        }

        window.UpdateLayout();
        SetLauncherPlacement(window, Session.Store.Layout("launcher", "topRatio"));
        window.Activate();
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.ForceForeground(hwnd);
        }
    }

    public bool TryReadFrame(Window window, out NativeMethods.WinRect rect)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            rect = default;
            return false;
        }

        return NativeMethods.GetWindowRect(hwnd, out rect);
    }

    /// <param name="syncWpf">只在呼出锁框时同步 WPF 属性；搜索过程中只打 SetWindowPos，避免布局回写顶边。</param>
    public void ApplyFrame(Window window, NativeMethods.WinRect rect, bool syncWpf)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        if (syncWpf)
        {
            SyncFrameToWpf(window, rect);
        }
    }

    void SetLauncherPlacement(Window window, double topRatio)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            Place(window, topRatio);
            return;
        }

        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = Session.Settings.MultiScreen switch
        {
            "main" => System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.FromPoint(cursor),
            "last" => System.Windows.Forms.Screen.AllScreens.FirstOrDefault(item => item.DeviceName == Session.Settings.LastScreen)
                      ?? System.Windows.Forms.Screen.FromPoint(cursor),
            _ => System.Windows.Forms.Screen.FromPoint(cursor)
        };
        Session.Settings.LastScreen = screen.DeviceName;
        var toDip = DipScale(window);
        var origin = toDip.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var size = toDip.Transform(new Point(screen.WorkingArea.Width, screen.WorkingArea.Height));
        const double inset = 16;
        var left = origin.X + (size.X - window.Width) / 2;
        var top = origin.Y + size.Y * topRatio;
        left = Math.Min(Math.Max(left, origin.X + inset), origin.X + size.X - window.Width - inset);
        top = Math.Min(Math.Max(top, origin.Y + inset), origin.Y + size.Y - window.Height - inset);

        var pos = ToPixels(window, new Point(left, top));
        var box = ToPixels(window, new Point(window.Width, window.Height));
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            (int)Math.Round(pos.X),
            (int)Math.Round(pos.Y),
            (int)Math.Round(box.X),
            (int)Math.Round(box.Y),
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        if (NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            SyncFrameToWpf(window, rect);
        }
    }

    void SyncFrameToWpf(Window window, NativeMethods.WinRect rect)
    {
        var fromDevice = DipScale(window);
        var topLeft = fromDevice.Transform(new Point(rect.Left, rect.Top));
        var bottomRight = fromDevice.Transform(new Point(rect.Right, rect.Bottom));
        window.Left = topLeft.X;
        window.Top = topLeft.Y;
        window.Width = Math.Max(1, bottomRight.X - topLeft.X);
        window.Height = Math.Max(1, bottomRight.Y - topLeft.Y);
    }

    System.Windows.Forms.Screen ScreenOf(Window window)
    {
        Point center;
        try
        {
            center = ToPixels(window, new Point(window.Left + window.Width / 2, window.Top + 8));
        }
        catch
        {
            center = new Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
        }

        return System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
    }

    public void Place(Window window, double topRatio)
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = Session.Settings.MultiScreen switch
        {
            "main" => System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.FromPoint(cursor),
            "last" => System.Windows.Forms.Screen.AllScreens.FirstOrDefault(item => item.DeviceName == Session.Settings.LastScreen)
                      ?? System.Windows.Forms.Screen.FromPoint(cursor),
            _ => System.Windows.Forms.Screen.FromPoint(cursor)
        };
        Session.Settings.LastScreen = screen.DeviceName;
        var area = screen.WorkingArea;
        var toDip = DipScale(window);
        var origin = toDip.Transform(new Point(area.Left, area.Top));
        var size = toDip.Transform(new Point(area.Width, area.Height));
        const double inset = 16;
        var x = origin.X + (size.X - window.Width) / 2;
        var y = origin.Y + size.Y * topRatio;
        window.Left = Math.Min(Math.Max(x, origin.X + inset), origin.X + size.X - window.Width - inset);
        window.Top = Math.Min(Math.Max(y, origin.Y + inset), origin.Y + size.Y - window.Height - inset);
    }

    /// <summary>第一次打开快捷启动时，窗口靠近鼠标，而不是回到屏幕固定比例。</summary>
    public void PlaceNearCursor(Window window)
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var toDip = DipScale(window);
        var mouse = toDip.Transform(new Point(cursor.X, cursor.Y));
        window.Left = mouse.X - Math.Min(180, window.Width / 3);
        window.Top = mouse.Y - 36;
        FitToScreen(window, screen);
        Log.Info($"快捷启动出现在鼠标附近 {window.Left:0},{window.Top:0}");
    }

    public void ApplySavedFrame(Window window, double x, double y, double width, double height)
    {
        window.Width = Math.Max(window.MinWidth, width);
        window.Height = Math.Max(window.MinHeight, height);
        window.Left = x;
        window.Top = y;
        var center = ToPixels(window, new Point(window.Left + window.Width / 2, window.Top + window.Height / 2));
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
        FitToScreen(window, screen);
    }

    void FitToScreen(Window window, System.Windows.Forms.Screen screen)
    {
        var toDip = DipScale(window);
        var area = screen.WorkingArea;
        var origin = toDip.Transform(new Point(area.Left, area.Top));
        var size = toDip.Transform(new Point(area.Width, area.Height));
        const double inset = 16;
        if (window.Width > size.X - inset * 2)
        {
            window.Width = Math.Max(window.MinWidth, size.X - inset * 2);
        }

        if (window.Height > size.Y - inset * 2)
        {
            window.Height = Math.Max(window.MinHeight, size.Y - inset * 2);
        }

        window.Left = Math.Min(Math.Max(window.Left, origin.X + inset), origin.X + size.X - window.Width - inset);
        window.Top = Math.Min(Math.Max(window.Top, origin.Y + inset), origin.Y + size.Y - window.Height - inset);
    }

    public Point ToPixels(Window window, Point dip)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget != null)
        {
            return source.CompositionTarget.TransformToDevice.Transform(dip);
        }

        var scale = 1d;
        try
        {
            using var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            scale = graphics.DpiX / 96d;
        }
        catch
        {
            scale = 1;
        }

        return new Point(dip.X * scale, dip.Y * scale);
    }

    static Matrix DipScale(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget != null)
        {
            return source.CompositionTarget.TransformFromDevice;
        }

        var dpi = 96d;
        try
        {
            using var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            dpi = graphics.DpiX;
        }
        catch
        {
            dpi = 96;
        }

        var scale = 96d / dpi;
        return new Matrix(scale, 0, 0, scale, 0, 0);
    }

    Button Button(string text, Action click, bool primary)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            Height = 28,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 0),
            Background = primary ? Brush("accent") : Brush("bg.subtle"),
            BorderBrush = primary ? Brush("accent") : Brush("border.default"),
            BorderThickness = new Thickness(1),
            Foreground = primary ? Brushes.White : Brush("text.secondary"),
            FontSize = primary ? 13 : 12,
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Medium,
            FontFamily = Font,
            Cursor = Cursors.Hand,
            Template = Flat()
        };
        button.Click += (_, _) => click();
        return button;
    }

    static ControlTemplate Flat()
    {
        if (flat != null)
        {
            return flat;
        }

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(System.Windows.Controls.Button.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(System.Windows.Controls.Button.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(System.Windows.Controls.Button.BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new Binding(nameof(System.Windows.Controls.Button.Padding)) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        flat = new ControlTemplate(typeof(System.Windows.Controls.Button)) { VisualTree = border };
        return flat;
    }

    static bool AppsUseLightTheme()
    {
        var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
        if (value is int coded)
        {
            return coded != 0;
        }

        return true;
    }
}
