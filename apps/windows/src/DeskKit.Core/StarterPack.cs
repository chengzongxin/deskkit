namespace DeskKit.Core;

public sealed record StarterSide(string Title, IReadOnlyList<string> Paths, IReadOnlyList<string> Names);

public sealed record StarterEntry(string Id, string Group, StarterSide Windows, StarterSide Macos);

public sealed record StarterGroupSpec(string Id, string Name, string Color);

public sealed record StarterCatalog(StarterGroupSpec Ungrouped, IReadOnlyList<StarterGroupSpec> Groups, IReadOnlyList<StarterEntry> Items);

public readonly record struct StarterFound(string Id, string Title, string Path, string GroupId);

public sealed class StarterShortcut
{
    public StarterShortcut(string id, string title, string path, string kind)
    {
        Id = id;
        Title = title;
        Path = path;
        Kind = kind;
    }

    public string Id { get; }
    public string Title { get; }
    public string Path { get; }
    public string Kind { get; }
}

public sealed class StarterBuilt
{
    public StarterBuilt(List<GroupRecord> groups, List<StarterShortcut> items)
    {
        Groups = groups;
        Items = items;
    }

    public List<GroupRecord> Groups { get; }
    public List<StarterShortcut> Items { get; }
}

public static class StarterPack
{
    public static StarterBuilt Compose(IReadOnlyList<StarterGroupSpec> groups, IReadOnlyList<StarterFound> found, StarterGroupSpec ungrouped)
    {
        var byGroup = new Dictionary<string, List<StarterFound>>(StringComparer.Ordinal);
        foreach (var item in found)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Path))
            {
                continue;
            }

            if (!byGroup.TryGetValue(item.GroupId, out var list))
            {
                list = [];
                byGroup[item.GroupId] = list;
            }

            list.Add(item);
        }

        var builtGroups = new List<GroupRecord>();
        var shortcuts = new List<StarterShortcut>();
        foreach (var group in groups)
        {
            if (group.Id == ungrouped.Id || !byGroup.TryGetValue(group.Id, out var list) || list.Count == 0)
            {
                continue;
            }

            var ids = new List<string>();
            foreach (var item in list)
            {
                ids.Add(item.Id);
                shortcuts.Add(new StarterShortcut(item.Id, item.Title, item.Path, KindOf(item.Path)));
            }

            builtGroups.Add(new GroupRecord(group.Id, group.Name, group.Color, ids));
        }

        builtGroups.Add(new GroupRecord(ungrouped.Id, ungrouped.Name, ungrouped.Color, Array.Empty<string>()));
        return new StarterBuilt(builtGroups, shortcuts);
    }

    public static string KindOf(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".app", StringComparison.OrdinalIgnoreCase))
        {
            return "app";
        }

        return "file";
    }
}
