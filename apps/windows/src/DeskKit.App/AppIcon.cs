using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskKit.Platform;

namespace DeskKit.App;

static class AppIcon
{
    const string EmbeddedName = "DeskKit.App.Assets.app-icon.ico";
    static Icon? master;
    static ImageSource? window;

    /// <summary>托盘专用：返回独立副本，由 <see cref="System.Windows.Forms.NotifyIcon"/> 持有，调用方不得 Dispose。</summary>
    public static Icon AllocateTrayIcon()
    {
        var source = Master();
        if (source == null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        return new Icon(source, new System.Drawing.Size(16, 16));
    }

    static Icon? Master()
    {
        if (master != null)
        {
            return master;
        }

        master = LoadIcon();
        return master;
    }

    public static ImageSource? WindowIcon()
    {
        if (window != null)
        {
            return window;
        }

        try
        {
            var path = DiskPath();
            if (path == null)
            {
                return null;
            }

            var frame = BitmapFrame.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            window = frame;
            return window;
        }
        catch (Exception ex)
        {
            Log.Warn($"设置窗口图标失败 {ex.Message}");
            return null;
        }
    }

    static Icon? LoadIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(EmbeddedName);
            if (stream != null)
            {
                var icon = new Icon(stream);
                Log.Info("托盘图标已加载（嵌入资源）");
                return icon;
            }

            var path = DiskPath();
            if (path != null)
            {
                var icon = new Icon(path);
                Log.Info($"托盘图标已加载 {path}");
                return icon;
            }

            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted != null)
                {
                    Log.Info($"托盘图标已加载（exe 嵌入） {Path.GetFileName(exe)}");
                    using (extracted)
                    {
                        return new Icon(extracted, new System.Drawing.Size(32, 32));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"读取应用图标失败 {ex.Message}");
        }

        Log.Warn("托盘图标回退为系统默认");
        return null;
    }

    static string? DiskPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        return File.Exists(path) ? path : null;
    }
}