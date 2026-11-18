using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeskKit.Core;
using DeskKit.Platform;

namespace DeskKit.App;

public sealed class ToolsWindow : Window
{
    readonly Shell shell;
    string mode = "grid";
    string active = "";
    string output = "";
    TextBlock? liveOutput;
    Chrome C => shell.Chrome;
    Session S => shell.Session;

    public ToolsWindow(Shell owner)
    {
        shell = owner;
        Title = "常用工具";
        C.Prepare(this);
        PreviewKeyDown += OnKey;
    }

    SolidColorBrush ToolCellFill(bool add)
    {
        var color = C.Brush(add ? "bg.subtle" : "bg.float").Color;
        var brush = new SolidColorBrush(Color.FromArgb(add ? (byte)255 : (byte)220, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public void ShowGrid()
    {
        mode = "grid";
        active = "";
        Width = S.Store.Layout("tools", "gridWidth");
        Height = S.Store.Layout("tools", "gridHeight");
        Render();
        shell.HideLauncher();
        C.Present(this, 0.12);
        Log.Info($"打开工具宫格 {Width:0}x{Height:0}");
    }

    public void ShowPage(string id)
    {
        mode = "page";
        active = id;
        S.Enter("TOOLPAGE");
        Width = S.Store.Layout("tools", "pageWidth");
        Height = S.Store.Layout("tools", "pageHeight");
        Render();
        shell.HideLauncher();
        C.Present(this, 0.12);
        Log.Info($"打开工具内页 {id} {Width:0}x{Height:0}");
    }

    public void Render()
    {
        if (mode == "page" && active.Length > 0) RenderPage();
        else RenderGrid();
    }

    public void CloseCurrent()
    {
        if (mode == "page")
        {
            ShowGrid();
            return;
        }

        Hide();
        S.Escape();
        if (S.Machine.State != "HIDDEN") shell.ShowLauncher();
    }

    void RenderGrid()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        var head = new DockPanel { Margin = new Thickness(16, 0, 8, 0) };
        var close = C.IconButton(Glyph.Close, CloseCurrent);
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(C.Label("常用工具", 16, FontWeights.SemiBold, "text.primary"));
        var hint = C.Label(S.Clips.Count == 0 ? "剪贴板：无新内容" : "剪贴板：1 条新", 12, FontWeights.Normal, "text.tertiary");
        hint.Margin = new Thickness(12, 0, 0, 0);
        head.Children.Add(hint);
        Grid.SetRow(head, 0);
        root.Children.Add(head);

        var grid = new UniformGrid { Rows = 3, Columns = 3, Margin = new Thickness(12, 0, 12, 8) };
        var order = new[] { "clipboard", "picker", "translate", "calc", "timestamp", "unit", "color", "qr", "add" };
        for (var i = 0; i < order.Length; i++)
        {
            var id = order[i];
            var number = i + 1;
            var cell = new Border
            {
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = C.Brush(id == "add" ? "border.strong" : "border.default"),
                Background = ToolCellFill(id == "add"),
                Cursor = Cursors.Hand
            };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = id == "add" ? Glyph.Plus : Glyph.ForTool(id),
                FontFamily = Chrome.IconFont,
                FontSize = 22,
                Foreground = C.Brush(id == "add" ? "text.tertiary" : "accent"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var label = C.Label(id == "add" ? "排序 / 显示" : Catalog.TitleOf(id), 12, FontWeights.Normal, "text.secondary");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            var num = C.Label(number.ToString(), 11, FontWeights.SemiBold, "text.tertiary");
            num.HorizontalAlignment = HorizontalAlignment.Right;
            num.Margin = new Thickness(0, 4, 6, 0);
            stack.Children.Add(num);
            cell.Child = stack;
            var toolId = id;
            cell.MouseLeftButtonUp += (_, _) =>
            {
                if (toolId == "add")
                {
                    S.Settings.HiddenTools = S.Settings.HiddenTools.Count == 0 ? ["qr"] : [];
                    S.Save();
                    Log.Info($"切换工具显示，隐藏 {string.Join(",", S.Settings.HiddenTools)}");
                    ShowGrid();
                }
                else
                {
                    ShowPage(toolId);
                }
            };
            grid.Children.Add(cell);
        }

        Grid.SetRow(grid, 1);
        root.Children.Add(grid);
        var foot = new Border { Background = C.Brush("bg.subtle") };
        var text = C.Label($"数字键 1–9 直达 · {S.Store.FormatChord("windows", "tools")} 打开面板 · Esc 返回", 11, FontWeights.Normal, "text.tertiary");
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        foot.Child = text;
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Content = C.Float(root, "bg.float");
    }

    void RenderPage()
    {
        var root = new DockPanel();
        var top = new DockPanel { Height = 48, LastChildFill = true };
        var back = C.IconButton(Glyph.Back, CloseCurrent);
        back.Width = 36;
        DockPanel.SetDock(back, Dock.Left);
        top.Children.Add(back);
        var title = C.Label(Catalog.TitleOf(active), 16, FontWeights.SemiBold, "text.primary");
        title.Margin = new Thickness(8, 0, 12, 0);
        DockPanel.SetDock(title, Dock.Left);
        top.Children.Add(title);
        var tag = C.Label("内置工具 · 离线可用", 11, FontWeights.SemiBold, "accent.hover");
        tag.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(tag);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var foot = new DockPanel { Height = 44, Background = C.Brush("bg.subtle"), LastChildFill = true };
        var copy = C.Primary("Enter 复制结果", CopyOutput);
        copy.Margin = new Thickness(16, 8, 8, 8);
        DockPanel.SetDock(copy, Dock.Left);
        foot.Children.Add(copy);
        foot.Children.Add(C.Label("Esc 返回宫格 · 本地计算，零网络请求", 12, FontWeights.Normal, "text.tertiary"));
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);
        root.Children.Add(PageBody());
        Content = C.Float(root, "bg.float");
    }

    UIElement PageBody()
    {
        return active switch
        {
            "calc" or "unit" => CalcPage(active == "unit"),
            "translate" => TranslatePage(),
            "color" => ColorPage(),
            "timestamp" => TimePage(),
            "clipboard" => ClipPage(),
            "picker" => PickerPage(),
            _ => NotePage("二维码", "二维码生成放在后续版本。现在可以把文本留在剪贴板。")
        };
    }

    UIElement CalcPage(bool unit)
    {
        var sample = unit ? "3km to mi" : "520+999-1314";
        output = unit ? UnitConverter.Convert(sample) ?? "—" : ExpressionEvaluator.Evaluate(sample).Data;
        var input = Field(sample);
        input.TextChanged += (_, _) => Publish(input.Text.Contains(" to ", StringComparison.OrdinalIgnoreCase) ? UnitConverter.Convert(input.Text) ?? "—" : (ExpressionEvaluator.Evaluate(input.Text).Ok ? ExpressionEvaluator.Evaluate(input.Text).Data : "—"));
        var chips = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var demo in new[] { "123*4", "520+999-1314", "(1+2)*3", "3km to mi" })
        {
            var text = demo;
            chips.Children.Add(C.Soft(text, () => { input.Text = text; Publish(text.Contains(" to ", StringComparison.OrdinalIgnoreCase) ? UnitConverter.Convert(text) ?? "—" : ExpressionEvaluator.Evaluate(text).Data); }));
        }

        var left = Pane("输入表达式 / 单位换算", input, chips, C.Label("支持四则运算与长度 / 重量 / 温度 / 离线汇率换算。", 11, FontWeights.Normal, "text.tertiary"));
        var result = C.Label(output, 36, FontWeights.SemiBold, "text.primary");
        liveOutput = result;
        result.HorizontalAlignment = HorizontalAlignment.Right;
        var right = Pane("结果", result, C.Label("在左侧输入算式，结果实时出现", 12, FontWeights.Normal, "text.tertiary"));
        return Split(left, right);
    }

    UIElement TranslatePage()
    {
        var translated = Translator.Translate("hello world");
        output = translated.Text;
        var input = Field("hello world");
        input.AcceptsReturn = true;
        input.Height = 120;
        input.TextWrapping = TextWrapping.Wrap;
        input.TextChanged += (_, _) => Publish(Translator.Translate(input.Text).Text);
        var left = Pane("输入（中英互译）", input, C.Label("触发词 fy 与此内页联动 · 内置离线小词典", 11, FontWeights.Normal, "text.tertiary"));
        var result = C.Label(output, 22, FontWeights.SemiBold, "accent.hover");
        liveOutput = result;
        result.TextWrapping = TextWrapping.Wrap;
        var right = Pane("译文", result, C.Label(S.Store.Text("translate.offline"), 12, FontWeights.Normal, "text.tertiary"));
        return Split(left, right);
    }

    UIElement ColorPage()
    {
        output = "#3B6CF6";
        var input = Field("#3B6CF6");
        var swatch = new Border { Height = 80, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 12, 0, 12), Background = new SolidColorBrush(Chrome.Paint("#3B6CF6")) };
        var desc = C.Label(ColorConvert.Describe("#3B6CF6") ?? "", 14, FontWeights.Medium, "text.primary");
        desc.TextWrapping = TextWrapping.Wrap;
        input.TextChanged += (_, _) =>
        {
            var hex = HexOf(input.Text) ?? "#CCCCCC";
            swatch.Background = new SolidColorBrush(Chrome.Paint(hex));
            output = hex;
            desc.Text = ColorConvert.Describe(input.Text) ?? "格式非法";
        };
        return Pane("HEX ↔ RGB ↔ HSL", input, swatch, desc);
    }

    UIElement TimePage()
    {
        output = TimestampConvert.Describe("1730000000") ?? "";
        var input = Field("1730000000");
        var desc = C.Label(output, 18, FontWeights.SemiBold, "text.primary");
        desc.TextWrapping = TextWrapping.Wrap;
        input.TextChanged += (_, _) =>
        {
            var text = TimestampConvert.Describe(input.Text) ?? "格式非法";
            output = text;
            desc.Text = text;
        };
        var now = C.Soft("取当前时间", () =>
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            input.Text = stamp;
            output = TimestampConvert.Describe(stamp) ?? stamp;
            desc.Text = output;
        });
        return Pane("时间戳 ↔ 日期", input, now, desc);
    }

    UIElement ClipPage()
    {
        var stack = new StackPanel();
        if (S.Clips.Count == 0)
        {
            stack.Children.Add(C.Label(S.Store.Text("empty.clipboard"), 13, FontWeights.Normal, "text.secondary"));
        }

        foreach (var clip in S.Clips.Take(8))
        {
            var text = clip.Text;
            stack.Children.Add(C.Soft(text.Length > 60 ? text[..60] : text, () =>
            {
                Session.Copy(text);
                output = text;
                Log.Info("已复制剪贴板记录");
            }));
        }

        stack.Children.Add(C.Soft("清空", () =>
        {
            S.Clips.Clear();
            S.Save();
            ShowPage("clipboard");
        }));
        return Pane("剪贴板历史", stack);
    }

    UIElement PickerPage()
    {
        var desc = C.Label(output.Length == 0 ? "—" : output, 22, FontWeights.SemiBold, "accent.hover");
        var pick = C.Primary("开始取色", () =>
        {
            shell.SuppressBlur = true;
            var picker = new PickerWindow();
            var ok = picker.ShowDialog() == true && picker.Hex != null;
            shell.SuppressBlur = false;
            if (!ok || picker.Hex == null)
            {
                Log.Warn("取色没有返回颜色");
                return;
            }

            output = picker.Hex;
            desc.Text = picker.Hex;
            Session.Copy(picker.Hex);
            Log.Info($"已复制取色 {picker.Hex}");
        });
        return Pane("截图取色", C.Label("点一下屏幕上的颜色，结果会复制到剪贴板。按 Esc 取消。", 13, FontWeights.Normal, "text.secondary"), pick, desc);
    }

    UIElement NotePage(string title, string note) => Pane(title, C.Label(note, 13, FontWeights.Normal, "text.secondary"));

    void Publish(string text)
    {
        output = text;
        if (liveOutput != null)
        {
            liveOutput.Text = text;
        }
    }

    void CopyOutput()
    {
        if (output.Length == 0) return;
        Session.Copy(output);
        Log.Info("已复制工具结果");
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CloseCurrent();
            e.Handled = true;
            return;
        }

        if (key == Key.Enter && mode == "page")
        {
            CopyOutput();
            e.Handled = true;
            return;
        }

        if (mode == "grid" && Digit(key) is int digit && digit is >= 1 and <= 9)
        {
            var order = new[] { "clipboard", "picker", "translate", "calc", "timestamp", "unit", "color", "qr", "add" };
            var id = order[digit - 1];
            if (id == "add")
            {
                S.Settings.HiddenTools = S.Settings.HiddenTools.Count == 0 ? ["qr"] : [];
                S.Save();
                ShowGrid();
            }
            else ShowPage(id);
            e.Handled = true;
        }
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

    Grid Split(UIElement left, UIElement right)
    {
        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        if (right is FrameworkElement rightElement)
        {
            rightElement.Margin = new Thickness(12, 0, 0, 0);
        }

        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    Border Pane(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        stack.Children.Add(C.Label(title, 12, FontWeights.SemiBold, "text.tertiary"));
        foreach (var child in children)
        {
            if (child is FrameworkElement element)
            {
                element.Margin = new Thickness(0, 10, 0, 0);
            }

            stack.Children.Add(child);
        }

        return new Border
        {
            Background = C.Brush("bg.float"),
            BorderBrush = C.Brush("border.default"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(16),
            Child = stack
        };
    }

    TextBox Field(string text) => new()
    {
        Text = text,
        Height = 32,
        FontSize = 15,
        FontWeight = FontWeights.Medium,
        FontFamily = Chrome.Font,
        VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(8, 0, 8, 0)
    };

    static string? HexOf(string input)
    {
        var rgb = ColorConvert.Parse(input);
        return rgb == null ? null : $"#{rgb.Value.R:X2}{rgb.Value.G:X2}{rgb.Value.B:X2}";
    }
}

public sealed class PickerWindow : Window
{
    public string? Hex { get; private set; }
    readonly TextBlock label = new()
    {
        Foreground = Brushes.White,
        Background = Brushes.Black,
        Padding = new Thickness(8, 4, 8, 4),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top
    };

    public PickerWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        Content = label;
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;
        SourceInitialized += (_, _) =>
        {
            // 屏幕坐标是物理像素，窗口的 Left/Width 是 DIP。150% 缩放下不换算会盖错屏幕。
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var origin = transform.Transform(new Point(area.Left, area.Top));
            var size = transform.Transform(new Point(area.Width, area.Height));
            Left = origin.X;
            Top = origin.Y;
            Width = Math.Max(1, size.X);
            Height = Math.Max(1, size.Y);
        };
        MouseMove += (_, _) => MoveLabel();
        MouseLeftButtonDown += (_, _) => Sample();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    void MoveLabel()
    {
        var point = Mouse.GetPosition(this);
        label.Margin = new Thickness(point.X + 16, point.Y + 16, 0, 0);
        label.Text = ReadHex() ?? "—";
    }

    void Sample()
    {
        Hex = ReadHex();
        DialogResult = Hex != null;
        Close();
    }

    static string? ReadHex()
    {
        try
        {
            var point = System.Windows.Forms.Cursor.Position;
            using var bitmap = new System.Drawing.Bitmap(1, 1);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(point.X, point.Y, 0, 0, new System.Drawing.Size(1, 1));
            var color = bitmap.GetPixel(0, 0);
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch (Exception ex)
        {
            Log.Warn($"取色失败 {ex.Message}");
            return null;
        }
    }
}
