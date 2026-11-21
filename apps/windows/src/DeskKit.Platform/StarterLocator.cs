namespace DeskKit.Platform;

public static class StarterLocator
{
    public readonly record struct Query(string Id, string GroupId, string Title, IReadOnlyList<string> Paths, IReadOnlyList<string> Names);

    public readonly record struct Hit(string Id, string GroupId, string Title, string Path);

    public static List<Hit> Find(IReadOnlyList<Query> items)
    {
        Dictionary<string, string>? names = null;
        var hits = new List<Hit>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                continue;
            }

            var path = Existing(item.Paths);
            if (path == null && item.Names.Count > 0)
            {
                names ??= StartMenuNames();
                path = Match(names, item.Names);
            }

            if (path == null)
            {
                continue;
            }

            hits.Add(new Hit(item.Id, item.GroupId, item.Title, path));
        }

        return hits;
    }

    public static List<Hit> Installed(IEnumerable<string> takenPaths)
    {
        var taken = new HashSet<string>(takenPaths.Where(path => !string.IsNullOrWhiteSpace(path)), StringComparer.OrdinalIgnoreCase);
        var hits = new List<Hit>();
        foreach (var root in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
        {
            var folder = Environment.GetFolderPath(root);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            var programs = Path.Combine(folder, "Programs");
            Collect(Directory.Exists(programs) ? programs : folder, hits, taken);
        }

        hits.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Title, right.Title));
        return hits.Count <= 150 ? hits : hits.GetRange(0, 150);
    }

    static string? Existing(IReadOnlyList<string> paths)
    {
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var expanded = Environment.ExpandEnvironmentVariables(raw);
            if (File.Exists(expanded))
            {
                return expanded;
            }
        }

        return null;
    }

    static Dictionary<string, string> StartMenuNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
        {
            var folder = Environment.GetFolderPath(root);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Walk(folder, map);
            }
        }

        return map;
    }

    static void Walk(string directory, Dictionary<string, string> map)
    {
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
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith("卸载", StringComparison.Ordinal) || name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!map.ContainsKey(name))
            {
                map[name] = file;
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
            Walk(child, map);
        }
    }

    static string? Match(Dictionary<string, string> map, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (map.TryGetValue(name, out var path))
            {
                return path;
            }
        }

        return null;
    }

    static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Startup", "启动",
        "Windows Administrative Tools", "Administrative Tools", "Windows 管理工具", "管理工具",
        "Windows Ease of Access", "Ease of Access", "Windows 轻松使用", "轻松使用", "辅助功能", "Accessibility",
        "Windows PowerShell", "Windows PowerShell (x86)",
        "Maintenance", "维护", "Windows Kits",
        "System Tools", "Windows 系统",
        "Accessories", "Windows Accessories", "Windows 附件", "附件"
    };

    static void Collect(string directory, List<Hit> hits, HashSet<string> taken)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.lnk").ToArray();
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            var title = Path.GetFileNameWithoutExtension(file);
            if (SkipTitle(title))
            {
                continue;
            }

            var target = ResolveShortcut(file);
            var stored = file;
            if (!string.IsNullOrWhiteSpace(target))
            {
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || Directory.Exists(target) || SkipTarget(target))
                {
                    continue;
                }

                if (File.Exists(target))
                {
                    stored = target;
                }
            }

            if (!taken.Add(stored))
            {
                continue;
            }

            hits.Add(new Hit(IdFor(stored), "apps", title, stored));
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
            if (SkipFolders.Contains(Path.GetFileName(child)))
            {
                continue;
            }

            Collect(child, hits, taken);
        }
    }

    static bool SkipTitle(string title)
    {
        if (title.StartsWith("卸载", StringComparison.Ordinal) || title.Contains("Uninstall", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return title.Contains("帮助", StringComparison.Ordinal)
            || title.Contains("自述", StringComparison.Ordinal)
            || title.Contains("Readme", StringComparison.OrdinalIgnoreCase)
            || title.Equals("Help", StringComparison.OrdinalIgnoreCase)
            || title.EndsWith(" Help", StringComparison.OrdinalIgnoreCase);
    }

    static bool SkipTarget(string target)
    {
        var name = Path.GetFileName(target);
        return name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("hh.exe", StringComparison.OrdinalIgnoreCase);
    }

    static string IdFor(string path)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return "app-" + Convert.ToHexString(bytes)[..12].ToLowerInvariant();
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
