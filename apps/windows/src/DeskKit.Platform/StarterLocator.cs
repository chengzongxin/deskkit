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
}
