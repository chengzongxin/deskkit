using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskKit.App;

public static class Icons
{
    static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? File(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
        {
            return null;
        }

        var key = path + "#" + size;
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
            image.Freeze();
            Cache[key] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }
}
