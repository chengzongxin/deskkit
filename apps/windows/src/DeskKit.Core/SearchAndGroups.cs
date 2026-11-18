namespace DeskKit.Core;

public sealed class IndexRow
{
    public IndexRow(string id, string kind, string title, IReadOnlyList<string> keywords)
    {
        Id = id;
        Kind = kind;
        Title = title;
        Keywords = keywords;
    }

    public string Id { get; }
    public string Kind { get; }
    public string Title { get; }
    public IReadOnlyList<string> Keywords { get; }
}

public static class KeywordMatcher
{
    static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "app", "file", "url", "tool" };

    public static List<string> Match(IReadOnlyList<IndexRow> rows, string query)
    {
        var needle = query.Trim();
        if (needle.Length == 0)
        {
            return new List<string>();
        }

        var hits = new List<string>();
        foreach (var row in rows)
        {
            if (!Kinds.Contains(row.Kind))
            {
                continue;
            }

            var titleHit = row.Title.Contains(needle, StringComparison.OrdinalIgnoreCase);
            var keywordHit = row.Keywords.Any(word => word.Contains(needle, StringComparison.OrdinalIgnoreCase));
            if (titleHit || keywordHit)
            {
                hits.Add(row.Id);
            }
        }

        return hits;
    }
}

public static class TriggerTable
{
    public static string? Resolve(ContractStore store, string word) => store.Trigger(word)?.ToolId;
}

public sealed class GroupRecord
{
    public GroupRecord(string id, string name, string color, IEnumerable<string> itemIds)
    {
        Id = id;
        Name = name;
        Color = color;
        ItemIds = itemIds.ToList();
    }

    public string Id { get; set; }
    public string Name { get; set; }
    public string Color { get; set; }
    public List<string> ItemIds { get; set; }
}

public sealed class ItemRecord
{
    public ItemRecord(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }
    public string Title { get; }
}

public static class GroupEditor
{
    public static DkResult Delete(List<GroupRecord> groups, IList<ItemRecord> items, string groupId, string ungroupedId, string _)
    {
        if (groupId == ungroupedId)
        {
            return DkResult.Fail("INVALID_ARG", "不能删除未分组");
        }

        var index = groups.FindIndex(group => group.Id == groupId);
        if (index < 0)
        {
            return DkResult.Fail("INVALID_ARG", "分组不存在");
        }

        var removing = new HashSet<string>(groups[index].ItemIds, StringComparer.Ordinal);
        groups.RemoveAt(index);
        foreach (var group in groups)
        {
            group.ItemIds.RemoveAll(id => removing.Contains(id));
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (removing.Contains(items[i].Id))
            {
                items.RemoveAt(i);
            }
        }

        return DkResult.Success(string.Join(",", removing));
    }
}
