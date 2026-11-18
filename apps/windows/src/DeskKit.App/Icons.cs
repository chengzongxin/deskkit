using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskKit.App;

public static class Icons
{
    static readonly ConcurrentDictionary<string, ImageSource> Exact = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, ImageSource> Generic = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentQueue<(string Path, int Size, Image Target)> Pending = new();
    static readonly ConcurrentDictionary<string, byte> Queued = new(StringComparer.OrdinalIgnoreCase);
    static int pumping;

    /// <summary>
    /// 先返回类型图标，输入不用等磁盘。应用和快捷方式的真图标在后台补上。
    /// </summary>
    public static ImageSource? File(string path, int size, Image? target = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var key = path + "#" + size;
        if (Exact.TryGetValue(key, out var exact))
        {
            return exact;
        }

        if (target != null && WantsExact(path))
        {
            Enqueue(path, size, key, target);
        }

        return ByKind(path, size);
    }

    static bool WantsExact(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }

    static ImageSource? ByKind(string path, int size)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var directory = extension.Length == 0 && System.IO.Directory.Exists(path);
        if (extension.Length == 0)
        {
            extension = "file";
        }

        var key = extension + "#" + size;
        if (Generic.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var probe = directory ? "folder" : "x" + extension;
        var attributes = directory ? 0x10u : 0x80u;
        var handle = IconHandle(probe, attributes, size, generic: true);
        ImageSource? image = null;
        if (handle != IntPtr.Zero)
        {
            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
                bitmap.Freeze();
                image = bitmap;
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        if (image != null)
        {
            Generic[key] = image;
        }

        return image;
    }

    static void Enqueue(string path, int size, string key, Image target)
    {
        if (Pending.Count > 16 || !Queued.TryAdd(key, 0))
        {
            return;
        }

        Pending.Enqueue((path, size, target));
        if (Interlocked.CompareExchange(ref pumping, 1, 0) == 0)
        {
            Task.Run(Pump);
        }
    }

    static void Pump()
    {
        try
        {
            while (Pending.TryDequeue(out var job))
            {
                var key = job.Path + "#" + job.Size;
                IntPtr handle = IntPtr.Zero;
                try
                {
                    var attributes = System.IO.Directory.Exists(job.Path) ? 0x10u : 0x80u;
                    handle = IconHandle(job.Path, attributes, job.Size, generic: false);
                }
                catch
                {
                    handle = IntPtr.Zero;
                }

                if (handle == IntPtr.Zero)
                {
                    Queued.TryRemove(key, out _);
                    continue;
                }

                var icon = handle;
                var target = job.Target;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(job.Size, job.Size));
                        image.Freeze();
                        Exact[key] = image;
                        target.Source = image;
                    }
                    finally
                    {
                        DestroyIcon(icon);
                        Queued.TryRemove(key, out _);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        finally
        {
            Interlocked.Exchange(ref pumping, 0);
            if (!Pending.IsEmpty && Interlocked.CompareExchange(ref pumping, 1, 0) == 0)
            {
                Task.Run(Pump);
            }
        }
    }

    static IntPtr IconHandle(string path, uint attributes, int size, bool generic)
    {
        var info = new ShFileInfo();
        var flags = 0x100u | (size <= 20 ? 0x1u : 0u);
        if (generic)
        {
            flags |= 0x10u;
        }

        var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
        return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr handle);
}
