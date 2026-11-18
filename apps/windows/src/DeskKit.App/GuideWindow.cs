using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskKit.Platform;

namespace DeskKit.App;

public sealed class GuideWindow : Window
{
    readonly Shell shell;
    int step = 1;
    Chrome C => shell.Chrome;
    Session S => shell.Session;

    public GuideWindow(Shell owner)
    {
        shell = owner;
        Title = "欢迎使用 DeskKit";
        Width = S.Store.Layout("windows", "guide", "width");
        Height = S.Store.Layout("windows", "guide", "height");
        C.Prepare(this);
    }

    public void Open()
    {
        step = 1;
        Render();
        C.Present(this, 0.12);
    }

    public void Render()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        var header = new Grid { Background = C.Brush("bg.float") };
        header.Children.Add(C.Label("DeskKit", 16, FontWeights.SemiBold, "text.primary"));
        ((TextBlock)header.Children[0]).Margin = new Thickness(24, 0, 0, 0);
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 1; i <= 3; i++)
        {
            dots.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Margin = new Thickness(4, 0, 4, 0),
                Fill = i == step ? C.Brush("accent") : C.Brush("border.strong")
            });
        }

        header.Children.Add(dots);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new StackPanel { Margin = new Thickness(48, 24, 48, 0) };
        var titles = new[] { "一个键，办完一件事", "设置呼出键", "权限与预置工具" };
        var chord = S.Store.FormatChord("windows", "summon");
        var bodies = new[]
        {
            "按一下呼出，找应用、找文件、翻译、计算、取色都在同一个框里。没有插件市场，内置工具开箱即用。",
            $"默认呼出键是 {chord}。如果这个组合被别的软件占用，可以到设置里改成别的键。",
            "Windows 这边需要开机自启和文件索引。未准备好时会写明状态，不会假装已经可用。预置 5 组工具包默认全选。"
        };
        body.Children.Add(C.Label(titles[step - 1], 22, FontWeights.SemiBold, "text.primary"));
        var text = C.Label(bodies[step - 1], 14, FontWeights.Normal, "text.secondary");
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(0, 16, 0, 16);
        body.Children.Add(text);
        if (step == 1)
        {
            body.Children.Add(C.Card("你会用到",
            [
                ("主启动器", "搜索应用 / 文件 / 网址 / 内置工具", (UIElement?)null),
                ("快捷启动面板", "分组图标网格", (UIElement?)null),
                ("常用工具", "剪贴板、翻译、计算器、取色", (UIElement?)null)
            ]));
        }
        else if (step == 2)
        {
            body.Children.Add(C.Label(chord, 32, FontWeights.SemiBold, "accent"));
            var status = shell.HotkeyReady ? "呼出键：已注册" : "呼出键：还没注册成功，可以改一个组合键";
            var line = C.Label(status, 13, FontWeights.Normal, "text.secondary");
            line.Margin = new Thickness(0, 12, 0, 12);
            body.Children.Add(line);
            body.Children.Add(C.Primary("打开快捷键设置", () => shell.ShowSettings("hotkeys")));
        }
        else
        {
            foreach (var item in S.Store.Permissions("windows"))
            {
                var granted = item.Id == "autostart" ? (S.Settings.AutoLaunch ? "已开启" : "可选") : S.Index.Count > 0 ? "已索引" : "即将开始";
                body.Children.Add(C.Card("", [(item.Title, item.Reason, C.Label(granted, 12, FontWeights.SemiBold, "text.secondary"))]));
            }
        }

        Grid.SetRow(body, 1);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var foot = new DockPanel { Background = C.Brush("bg.float"), LastChildFill = false };
        foot.Children.Add(C.Soft("跳过", Finish));
        var next = C.Primary(step == 3 ? "开始使用" : "下一步", () =>
        {
            if (step < 3) { step++; Render(); }
            else Finish();
        });
        DockPanel.SetDock(next, Dock.Right);
        foot.Children.Add(next);
        if (step > 1)
        {
            var prev = C.Soft("上一步", () => { step = Math.Max(1, step - 1); Render(); });
            DockPanel.SetDock(prev, Dock.Right);
            foot.Children.Add(prev);
        }

        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Content = C.Float(root, "bg.canvas");
        Log.Info($"引导 Step {step}");
    }

    void Finish()
    {
        S.Settings.GuideDone = true;
        S.Save();
        Hide();
        S.Summon();
        Log.Info("引导完成");
    }
}
