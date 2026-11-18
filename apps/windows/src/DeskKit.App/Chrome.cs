using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public Chrome(Session session) => Session = session;

    public static FontFamily Font { get; } = new("Microsoft YaHei UI, Segoe UI");
    public static FontFamily IconFont { get; } = new("Segoe MDL2 Assets");

    public bool Dark => Session.Settings.Theme switch
    {
        "dark" => true,
        "system" => !AppsUseLightTheme(),
        _ => false
    };

    public void Invalidate() => cache.Clear();

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

    public void PaintBackground(Border border, string fill)
    {
        var alpha = Math.Min(1, Math.Max(0.15, Session.Settings.BackgroundOpacity));
        var path = Session.Settings.BackgroundImagePath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                border.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill, Opacity = alpha };
                return;
            }
            catch (Exception ex)
            {
                Log.Warn($"背景图无法读取 {ex.Message}");
            }
        }

        var color = Brush(fill).Color;
        border.Background = new SolidColorBrush(MediaColor.FromArgb((byte)(alpha * 255), color.R, color.G, color.B));
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
        window.ShowInTaskbar = false;
        window.Topmost = true;
        window.ResizeMode = ResizeMode.NoResize;
        window.FontFamily = Font;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            NativeMethods.MakeToolWindow(hwnd);
        };
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
