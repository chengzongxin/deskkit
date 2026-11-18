using Microsoft.Win32;

namespace DeskKit.Platform;

public sealed class IndexedEntry
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Path { get; init; } = "";
    public string[] Keywords { get; init; } = [];
    public int Score { get; init; }
}

/// <summary>
/// 应用来自开始菜单、桌面快捷方式和 App Paths。文件只扫桌面、文档、下载，和 macOS 一样限制深度。
/// </summary>
public static class Indexer
{
    public static List<IndexedEntry> Collect()
    {
        // 解析 .lnk 用的是 COM。后台线程默认不是 STA，这里单独开一条 STA 线程。
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return CollectCore();
        }

        List<IndexedEntry>? result = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = CollectCore();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
        {
            throw error;
        }

        return result ?? [];
    }

    static List<IndexedEntry> CollectCore()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = new List<IndexedEntry>();
        AddShortcuts(hits, seen);
        AddAppPaths(hits, seen);
        AddFiles(hits, seen);
        Log.Info($"索引完成，{hits.Count} 条");
        return hits;
    }

    static void AddShortcuts(List<IndexedEntry> hits, HashSet<string> seen)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };
        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
        {
            WalkShortcuts(root, hits, seen);
            if (hits.Count > 1500)
            {
                return;
            }
        }
    }

    static void WalkShortcuts(string directory, List<IndexedEntry> hits, HashSet<string> seen)
    {
        if (hits.Count > 1500)
        {
            return;
        }

        // 开始菜单里有无权限的联接点。整树枚举会在中途抛错，所以按目录单独跳过。
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.lnk");
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            try
            {
                var title = System.IO.Path.GetFileNameWithoutExtension(file);
                if (title.StartsWith("卸载", StringComparison.Ordinal) || title.Contains("Uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = ResolveShortcut(file) ?? file;
                var kind = target.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "url" : "app";
                Add(hits, seen, kind == "url" ? "url-" + target : "app-" + file, kind, title, target, file, 30);
            }
            catch (Exception ex)
            {
                Log.Warn($"跳过快捷方式 {ex.Message}");
            }

            if (hits.Count > 1500)
            {
                return;
            }
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(directory).ToArray();
        }
        catch
        {
            return;
        }

        foreach (var child in children)
        {
            WalkShortcuts(child, hits, seen);
        }
    }

    static void AddAppPaths(List<IndexedEntry> hits, HashSet<string> seen)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                if (key == null)
                {
                    continue;
                }

                foreach (var name in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(name);
                    if (sub?.GetValue("") is not string path || !File.Exists(path))
                    {
                        continue;
                    }

                    var title = System.IO.Path.GetFileNameWithoutExtension(name);
                    Add(hits, seen, "app-" + path, "app", title, path, path, 28);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"读取 App Paths 失败 {ex.Message}");
            }
        }
    }

    static void AddFiles(List<IndexedEntry> hits, HashSet<string> seen)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[] { "Desktop", "Documents", "Downloads" }.Select(name => System.IO.Path.Combine(home, name));
        var count = 0;
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(root);
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                Append(child, hits, seen, depth: 0, ref count);
                if (count > 600)
                {
                    return;
                }
            }
        }
    }

    static void Append(string path, List<IndexedEntry> hits, HashSet<string> seen, int depth, ref int count)
    {
        if (count > 600)
        {
            return;
        }

        var name = System.IO.Path.GetFileName(path);
        if (name.StartsWith('.') || name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Add(hits, seen, "file-" + path, "file", name, path, path, 20);
        count++;
        if (depth >= 1 || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path).Take(40))
            {
                Append(child, hits, seen, depth + 1, ref count);
            }
        }
        catch
        {
            // 单个目录读不到就跳过，不让索引中断。
        }
    }

    static void Add(List<IndexedEntry> hits, HashSet<string> seen, string id, string kind, string title, string subtitle, string path, int score)
    {
        if (!seen.Add(path))
        {
            return;
        }

        var words = new List<string> { title.ToLowerInvariant() };
        try
        {
            words.AddRange(Pinyin.Variants(title));
        }
        catch (Exception ex)
        {
            Log.Warn($"拼音失败 {ex.Message}");
        }
        hits.Add(new IndexedEntry
        {
            Id = id,
            Kind = kind,
            Title = title,
            Subtitle = subtitle,
            Path = path,
            Keywords = words.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Score = score
        });
    }

    static string? ResolveShortcut(string path)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(path);
            string target = shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }
}
