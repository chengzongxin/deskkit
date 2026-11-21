using System.Globalization;
using DeskKit.Core;
using DeskKit.Platform;

namespace DeskKit.App;

/// <summary>
/// 一次呼出里的数据：设置、分组、剪贴板、搜索结果。界面只通过它改状态。
/// </summary>
public sealed class Session
{
    public ContractStore Store { get; }
    public LauncherStateMachine Machine { get; }
    public Settings Settings { get; set; } = new();
    public Library Library { get; set; } = DefaultLibrary();
    public List<Clip> Clips { get; set; } = [];
    public List<Hit> Index
    {
        get => index;
        set
        {
            index = value ?? [];
            // 搜索每次输入都会用到这份表。提前做好，避免每敲一个字都重新分配。
            searchRows = index.Select(hit => new IndexRow(hit.Id, hit.Kind, hit.Title, hit.Keywords)).ToArray();
        }
    }
    public List<string> Recents { get; set; } = [];
    public string Query { get; set; } = "";
    public string TriggerWord { get; set; } = "";
    public string Payload { get; set; } = "";
    public List<Hit> Hits { get; set; } = [];
    public int Selection { get; set; }
    public Action OnChange { get; set; } = () => { };
    public Action? OnShow { get; set; }
    public Action? OnHide { get; set; }
    public Action<string>? OnOpenTool { get; set; }
    DateTime shownAt = DateTime.MinValue;
    List<Hit> index = [];
    IndexRow[] searchRows = [];

    public Session(ContractStore store)
    {
        Store = store;
        Machine = new LauncherStateMachine(store, Log.Info);
        Load();
        Log.Info($"契约已加载，哈希 {store.Hash}");
    }

    public void Load()
    {
        Settings = JsonStore.Read<Settings>("settings.json") ?? new Settings();
        Settings.BackgroundOpacity = Math.Min(1, Math.Max(0.15, Settings.BackgroundOpacity == 0 ? 1 : Settings.BackgroundOpacity));
        if (Settings.ToolOrder.Count == 0)
        {
            Settings.ToolOrder = Catalog.Tools.Select(tool => tool.Id).Append("add").ToList();
        }

        Library = JsonStore.Read<Library>("groups.json") ?? DefaultLibrary();
        if (Library.Groups.Count == 0)
        {
            Library = DefaultLibrary();
        }

        Clips = JsonStore.Read<List<Clip>>("clipboard.json") ?? [];
        Recents = JsonStore.Read<List<string>>("recents.json") ?? [];

        if (PurgeReservedNavGroup())
        {
            JsonStore.Write(Library, "groups.json");
        }

        SeedStarterIfNeeded();
    }

    void SeedStarterIfNeeded()
    {
        if (Settings.StarterSeeded)
        {
            return;
        }

        if (Library.Items.Count == 0)
        {
            var added = ApplyStarter(merge: false);
            Log.Info($"初始快捷启动已放入 {added} 项");
        }

        Settings.StarterSeeded = true;
        Save();
    }

    public int ImportStarterTools()
    {
        var added = ApplyStarter(merge: true);
        Settings.StarterSeeded = true;
        Save();
        OnChange();
        Log.Info(added == 0 ? "系统工具都已经在快捷启动里" : $"已放入 {added} 个系统工具");
        return added;
    }

    int ApplyStarter(bool merge)
    {
        var catalog = Store.Starter();
        var hits = StarterLocator.Find(catalog.Items.Select(item => new StarterLocator.Query(
            item.Id,
            item.Group,
            item.Windows.Title,
            item.Windows.Paths,
            item.Windows.Names)).ToArray());
        var built = StarterPack.Compose(
            catalog.Groups,
            hits.Select(hit => new StarterFound(hit.Id, hit.Title, hit.Path, hit.GroupId)).ToArray(),
            catalog.Ungrouped);
        if (!merge)
        {
            Library.SchemaVersion = 1;
            Library.Groups = built.Groups.Select(group => new Group
            {
                Id = group.Id,
                Name = group.Name,
                Color = group.Color,
                ItemIds = group.ItemIds.ToList()
            }).ToList();
            Library.Items = built.Items.Select(item => new Shortcut
            {
                Id = item.Id,
                Title = item.Title,
                Path = item.Path,
                Kind = item.Kind
            }).ToList();
            return Library.Items.Count;
        }

        foreach (var group in built.Groups)
        {
            if (group.Id == catalog.Ungrouped.Id || Library.Groups.Any(existing => existing.Id == group.Id))
            {
                continue;
            }

            var ungrouped = Library.Groups.FindIndex(existing => existing.Id == catalog.Ungrouped.Id);
            var created = new Group { Id = group.Id, Name = group.Name, Color = group.Color, ItemIds = [] };
            if (ungrouped >= 0)
            {
                Library.Groups.Insert(ungrouped, created);
            }
            else
            {
                Library.Groups.Add(created);
            }
        }

        var known = new HashSet<string>(Library.Items.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var item in built.Items)
        {
            if (known.Contains(item.Path))
            {
                continue;
            }

            var group = built.Groups.FirstOrDefault(candidate => candidate.ItemIds.Contains(item.Id));
            if (group == null)
            {
                continue;
            }

            var id = Library.Items.Any(existing => existing.Id == item.Id) ? Guid.NewGuid().ToString() : item.Id;
            Library.Items.Add(new Shortcut { Id = id, Title = item.Title, Path = item.Path, Kind = item.Kind });
            var index = Library.Groups.FindIndex(existing => existing.Id == group.Id);
            if (index >= 0)
            {
                Library.Groups[index].ItemIds.Add(id);
            }

            known.Add(item.Path);
            added++;
        }

        return added;
    }

    public void Save()
    {
        JsonStore.Write(Settings, "settings.json");
        JsonStore.Write(Library, "groups.json");
        JsonStore.Write(Clips, "clipboard.json");
        JsonStore.Write(Recents, "recents.json");
    }

    public void NoteClipboard(string? text, IReadOnlyList<string> files)
    {
        if (Settings.ClipboardPaused)
        {
            return;
        }

        string body;
        string kind;
        if (!string.IsNullOrEmpty(text))
        {
            body = text;
            kind = "text";
        }
        else if (files.Count > 0)
        {
            body = string.Join("\n", files);
            kind = "file";
        }
        else
        {
            return;
        }

        if (Clips.Count > 0 && Clips[0].Text == body)
        {
            return;
        }

        Clips.Insert(0, new Clip
        {
            Id = Guid.NewGuid().ToString(),
            Kind = kind,
            Text = body,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Favorite = false
        });
        if (Clips.Count > 200)
        {
            Clips.RemoveRange(200, Clips.Count - 200);
        }

        Save();
        PruneClips();
        Log.Info($"记录剪贴板 {kind}，长度 {body.Length}");
        OnChange();
    }

    public void Search()
    {
        var raw = Query;
        var parsed = ParseTrigger(raw);
        if (parsed != null)
        {
            TriggerWord = parsed.Value.Word;
            Payload = parsed.Value.Payload;
            if (Machine.State != "TOOL")
            {
                Go("TOOL");
            }

            Hits = ToolModeHits(parsed.Value.ToolId, parsed.Value.Payload);
            Selection = 0;
            return;
        }

        TriggerWord = "";
        Payload = "";
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            Hits = [];
            if (Machine.State != "HIDDEN")
            {
                Go("A1");
            }

            return;
        }

        var found = new List<Hit>();
        if (LooksLikeExpression(trimmed))
        {
            var value = ExpressionEvaluator.Evaluate(trimmed);
            if (value.Ok)
            {
                found.Add(ToolHit("calc", "= " + value.Data, "计算器", "calc", value.Data, 100));
            }
        }

        var unit = UnitConverter.Convert(trimmed);
        if (unit != null)
        {
            found.Add(ToolHit("unit", unit, "单位换算", "unit", unit, 95));
        }

        if ((trimmed.StartsWith('#') || (trimmed.Length == 6 && trimmed.All(Uri.IsHexDigit))) && ColorConvert.Describe(trimmed) is string color)
        {
            found.Add(ToolHit("color", color, "颜色转换", "color", color, 90));
        }

        if (trimmed.All(char.IsDigit) && trimmed.Length is 10 or 13 && TimestampConvert.Describe(trimmed) is string time)
        {
            found.Add(ToolHit("ts", time, "时间戳", "timestamp", time, 90));
        }

        var ids = KeywordMatcher.Match(searchRows, trimmed).ToHashSet(StringComparer.Ordinal);
        var matched = Index.Where(hit => ids.Contains(hit.Id)).OrderByDescending(hit => hit.Score).ToList();
        foreach (var kind in new[] { "tool", "app", "file", "url" })
        {
            found.AddRange(matched.Where(hit => hit.Kind == kind));
        }

        Hits = found;
        Log.Info($"搜索「{trimmed}」命中 {Hits.Count} 条，不截断");
        Selection = 0;
        Go(Hits.Count == 0 ? "A4" : "A3");
    }

    public List<Hit> RecentHits()
    {
        var rows = new List<Hit>();
        foreach (var id in Recents)
        {
            if (TryResolveRecent(id) is Hit hit)
            {
                rows.Add(hit);
            }

            if (rows.Count >= 6)
            {
                break;
            }
        }

        return rows;
    }

    public void TouchRecentFromPath(string path, string kind, string title)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var hit = Index.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (hit != null)
        {
            Remember(hit.Id);
            return;
        }

        Remember(RecentPathKey(path));
    }

    static string RecentPathKey(string path) => "path:" + path;

    Hit? TryResolveRecent(string id)
    {
        var hit = Index.FirstOrDefault(item => item.Id == id);
        if (hit != null)
        {
            return hit;
        }

        if (id.StartsWith("path:", StringComparison.Ordinal))
        {
            var path = id[5..];
            hit = Index.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                return hit;
            }

            return HitFromStoredPath(path);
        }

        var shortcut = Library.Items.FirstOrDefault(item => item.Id == id);
        if (shortcut == null)
        {
            return null;
        }

        hit = Index.FirstOrDefault(item => string.Equals(item.Path, shortcut.Path, StringComparison.OrdinalIgnoreCase));
        return hit ?? new Hit
        {
            Id = shortcut.Id,
            Kind = shortcut.Kind,
            Title = shortcut.Title,
            Subtitle = shortcut.Kind switch { "url" => "网址", "app" => "应用", _ => "文件" },
            Path = shortcut.Path,
            Score = 20
        };
    }

    static Hit? HitFromStoredPath(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var host = Uri.TryCreate(path, UriKind.Absolute, out var uri) ? uri.Host : path;
            return new Hit { Id = "url-" + path, Kind = "url", Title = host, Subtitle = "网址", Path = path, Score = 20 };
        }

        var title = System.IO.Path.GetFileNameWithoutExtension(path);
        if (title.Length == 0)
        {
            title = path;
        }

        var kind = System.IO.Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? "app"
            : "file";
        return new Hit
        {
            Id = RecentPathKey(path),
            Kind = kind,
            Title = title,
            Subtitle = kind == "app" ? "应用" : "文件",
            Path = path,
            Score = 20
        };
    }

    public void Execute(Hit hit)
    {
        if (hit.CopyText != null)
        {
            Copy(hit.CopyText);
            Log.Info("已复制结果");
        }
        else if (hit.Kind == "tool" && hit.ToolId != null)
        {
            Log.Info($"打开工具 {hit.ToolId}");
            OnOpenTool?.Invoke(hit.ToolId);
            return;
        }
        else if (!string.IsNullOrEmpty(hit.Path))
        {
            Open(hit.Path);
            Log.Info($"已打开 {hit.Title}");
        }

        Remember(hit.Id);
        Hide();
    }

    public void ExecuteSelection()
    {
        if (Hits.Count == 0)
        {
            return;
        }

        Execute(Hits[Math.Clamp(Selection, 0, Hits.Count - 1)]);
    }

    public void Escape()
    {
        Machine.Escape();
        if (Machine.State == "HIDDEN")
        {
            OnHide?.Invoke();
        }
        else
        {
            OnChange();
        }
    }

    public void Summon()
    {
        if (Machine.State == "HIDDEN")
        {
            shownAt = DateTime.UtcNow;
            Machine.Transition("A1");
            Query = "";
            TriggerWord = "";
            Payload = "";
            Hits = [];
            Log.Info("呼出浮层");
            OnShow?.Invoke();
        }
        else if ((DateTime.UtcNow - shownAt).TotalSeconds < 0.45)
        {
            Log.Info("忽略刚呼出后的重复热键");
        }
        else
        {
            Hide();
        }
    }

    public void Hide()
    {
        if (Machine.State != "HIDDEN")
        {
            Machine.Transition("HIDDEN");
        }

        Log.Info("隐藏浮层");
        OnHide?.Invoke();
    }

    public void ReorderGroup(string sourceId, string targetId, bool placeAfter = false)
    {
        if (sourceId == targetId)
        {
            return;
        }

        var from = Library.Groups.FindIndex(group => group.Id == sourceId);
        var to = Library.Groups.FindIndex(group => group.Id == targetId);
        if (from < 0 || to < 0)
        {
            return;
        }

        var group = Library.Groups[from];
        Library.Groups.RemoveAt(from);
        if (from < to)
        {
            to--;
        }

        if (placeAfter)
        {
            to++;
        }

        Library.Groups.Insert(to, group);
        Save();
        Log.Info($"分组已移动到「{Library.Groups[to].Name}」位置");
        OnChange();
    }

    public void MoveShortcut(string itemId, string sourceGroupId, string targetGroupId)
    {
        if (sourceGroupId == targetGroupId)
        {
            return;
        }

        var source = Library.Groups.FindIndex(group => group.Id == sourceGroupId);
        var target = Library.Groups.FindIndex(group => group.Id == targetGroupId);
        if (source < 0 || target < 0 || !Library.Groups[source].ItemIds.Contains(itemId))
        {
            return;
        }

        Library.Groups[source].ItemIds.RemoveAll(id => id == itemId);
        if (!Library.Groups[target].ItemIds.Contains(itemId))
        {
            Library.Groups[target].ItemIds.Add(itemId);
        }

        Save();
        var name = Library.Items.FirstOrDefault(item => item.Id == itemId)?.Title ?? itemId;
        Log.Info($"已将「{name}」从「{Library.Groups[source].Name}」移到「{Library.Groups[target].Name}」");
        OnChange();
    }

    public void ReorderShortcut(string itemId, string groupId, string beforeId, bool placeAfter)
    {
        if (itemId == beforeId)
        {
            return;
        }

        var index = Library.Groups.FindIndex(group => group.Id == groupId);
        if (index < 0)
        {
            return;
        }

        var ids = Library.Groups[index].ItemIds;
        var from = ids.IndexOf(itemId);
        if (from < 0)
        {
            return;
        }

        ids.RemoveAt(from);
        var anchor = ids.IndexOf(beforeId);
        if (anchor < 0)
        {
            return;
        }

        var insert = placeAfter ? anchor + 1 : anchor;
        ids.Insert(Math.Min(insert, ids.Count), itemId);
        Save();
        Log.Info("快捷项已在分组内调整顺序");
        OnChange();
    }

    public void RenameGroup(string id, string name)
    {
        var trimmed = name.Trim();
        var index = Library.Groups.FindIndex(group => group.Id == id);
        if (index < 0 || trimmed.Length == 0)
        {
            Log.Warn("分组名称不能为空");
            return;
        }

        if (Library.Groups[index].Name == trimmed) return;
        Library.Groups[index].Name = trimmed;
        Save();
        Log.Info($"分组已重命名为「{trimmed}」");
        OnChange();
    }

    public void DeleteGroup(string id)
    {
        if (id == "ungrouped")
        {
            Log.Warn("不能删除未分组");
            return;
        }

        var group = Library.Groups.FirstOrDefault(entry => entry.Id == id);
        if (group == null)
        {
            Log.Warn("分组不存在");
            return;
        }

        var records = Library.Groups.Select(entry => new GroupRecord(entry.Id, entry.Name, entry.Color, entry.ItemIds)).ToList();
        var items = Library.Items.Select(item => new ItemRecord(item.Id, item.Title)).ToList();
        var removed = GroupEditor.Delete(records, items, id, "ungrouped", "未分组");
        if (!removed.Ok)
        {
            Log.Warn(removed.Message);
            return;
        }

        var remaining = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        Library.Groups = records.Select(record => new Group
        {
            Id = record.Id,
            Name = record.Name,
            Color = string.IsNullOrEmpty(record.Color) ? "#98A1B0" : record.Color,
            ItemIds = record.ItemIds
        }).ToList();
        Library.Items.RemoveAll(item => !remaining.Contains(item.Id));
        Save();
        Log.Info($"已删除分组「{group.Name}」，并移除 {group.ItemIds.Count} 个快捷项");
        OnChange();
    }

    public bool AddShortcut(string path, string groupId)
    {
        var full = path.Trim().TrimEnd('\\', '/');
        if (!File.Exists(full) && !Directory.Exists(full))
        {
            Log.Warn($"快捷项路径不存在 {full}");
            return false;
        }

        var directory = Directory.Exists(full);
        var extension = System.IO.Path.GetExtension(full);
        var kind = !directory && (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) ? "app" : "file";
        var title = kind == "app" ? System.IO.Path.GetFileNameWithoutExtension(full) : System.IO.Path.GetFileName(full);
        var label = kind == "app" ? "应用" : directory ? "文件夹" : "文件";
        return PutShortcut(full, string.IsNullOrEmpty(title) ? full : title, kind, label, groupId);
    }

    public bool AddUrl(string url, string groupId)
    {
        var text = url.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            Log.Warn($"不能放入快捷启动 {text}");
            return false;
        }

        var title = string.IsNullOrEmpty(parsed.Host) ? text : parsed.Host;
        return PutShortcut(text, title, "url", "网址", groupId);
    }

    public (string ToolId, string Label, string Placeholder)? ResolveTrigger(string word)
    {
        var custom = Settings.TriggerWords.FirstOrDefault(pair => string.Equals(pair.Value, word, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(custom.Key))
        {
            var built = Store.Triggers.FirstOrDefault(item => item.ToolId == custom.Key);
            if (built != null)
            {
                return (built.ToolId, built.Label, built.Placeholder);
            }
        }

        var found = Store.Trigger(word);
        if (found == null)
        {
            return null;
        }

        if (Settings.TriggerWords.TryGetValue(found.Value.ToolId, out var renamed)
            && !string.Equals(renamed, word, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return found;
    }

    public void PruneClips()
    {
        double? days = Settings.ClipboardRetention switch
        {
            "30d" => 30,
            "forever" => null,
            _ => 7
        };
        if (days == null)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-days.Value);
        var before = Clips.Count;
        Clips.RemoveAll(clip =>
        {
            if (!DateTimeOffset.TryParse(clip.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            {
                date = DateTimeOffset.UtcNow;
            }

            return date < cutoff;
        });
        if (Clips.Count != before)
        {
            Save();
            Log.Info($"按保留策略清理剪贴板，剩余 {Clips.Count} 条");
        }
    }

    public void Enter(string next) => Machine.Reach(next);

    public static void Copy(string text)
    {
        System.Windows.Clipboard.SetText(text);
    }

    public static void Open(string path)
    {
        if (!path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !File.Exists(path)
            && !Directory.Exists(path))
        {
            Log.Warn($"快捷项已经不在原位置 {path}");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static void Reveal(string path)
    {
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return;
        }

        var folder = Path.GetDirectoryName(path);
        if (File.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }

    bool PutShortcut(string stored, string title, string kind, string label, string groupId)
    {
        var target = ResolveGroup(groupId);
        var existing = Library.Items.FirstOrDefault(item => string.Equals(item.Path, stored, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var index = Library.Groups.FindIndex(group => group.Id == target);
            if (index < 0)
            {
                return false;
            }

            if (Library.Groups[index].ItemIds.Contains(existing.Id))
            {
                Log.Info($"快捷项已在这个分组里，跳过 {existing.Title}");
                return false;
            }

            Library.Groups[index].ItemIds.Add(existing.Id);
            Save();
            OnChange();
            Log.Info($"快捷项已放入「{Library.Groups[index].Name}」{existing.Title}");
            return true;
        }

        var id = Guid.NewGuid().ToString();
        Library.Items.Add(new Shortcut { Id = id, Title = title, Path = stored, Kind = kind });
        AppendToGroup(target, id);
        var whereName = Library.Groups.FirstOrDefault(group => group.Id == target)?.Name ?? target;
        Log.Info($"已添加快捷项 {title}（{label}）到「{whereName}」");
        return true;
    }

    string ResolveGroup(string groupId)
    {
        if (Library.Groups.Any(group => group.Id == groupId))
        {
            return groupId;
        }

        if (Library.Groups.Any(group => group.Id == "ungrouped"))
        {
            return "ungrouped";
        }

        if (Library.Groups.Count > 0)
        {
            return Library.Groups[0].Id;
        }

        Library.Groups.Add(new Group { Id = "ungrouped", Name = "未分组", Color = "#98A1B0", ItemIds = [] });
        return "ungrouped";
    }

    void AppendToGroup(string groupId, string id)
    {
        var index = Library.Groups.FindIndex(group => group.Id == groupId);
        if (index >= 0)
        {
            Library.Groups[index].ItemIds.Add(id);
        }

        Save();
        OnChange();
    }

    void Remember(string id)
    {
        Recents.RemoveAll(item => item == id);
        Recents.Insert(0, id);
        if (Recents.Count > 20)
        {
            Recents.RemoveRange(20, Recents.Count - 20);
        }

        Save();
    }

    void Go(string next) => Machine.Reach(next);

    (string Word, string Payload, string ToolId)? ParseTrigger(string raw)
    {
        var space = raw.IndexOf(' ');
        if (space <= 0)
        {
            return null;
        }

        var word = raw[..space];
        var found = ResolveTrigger(word);
        if (found == null)
        {
            return null;
        }

        return (word, raw[(space + 1)..], found.Value.ToolId);
    }

    List<Hit> ToolModeHits(string toolId, string payload)
    {
        switch (toolId)
        {
            case "translate":
                var translated = Translator.Translate(payload);
                return [ToolHit("tr", translated.Text, Store.Text("translate.offline"), "translate", translated.Text, 100)];
            case "calc":
                var unit = UnitConverter.Convert(payload);
                if (unit != null)
                {
                    return [ToolHit("unit", unit, "单位换算", "unit", unit, 100)];
                }

                var value = ExpressionEvaluator.Evaluate(payload);
                return [ToolHit("calc", value.Ok ? "= " + value.Data : value.Message, "计算器", "calc", value.Ok ? value.Data : null, 100)];
            case "color":
                var color = ColorConvert.Describe(payload) ?? "无法识别颜色";
                return [ToolHit("color", color, "颜色转换", "color", color, 100)];
            case "timestamp":
                var time = TimestampConvert.Describe(payload) ?? "无法识别时间";
                return [ToolHit("ts", time, "时间戳", "timestamp", time, 100)];
            case "clipboard":
                var needle = payload.ToLowerInvariant();
                return Clips.Where(clip => needle.Length == 0 || clip.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)).Take(8)
                    .Select(clip => ToolHit(clip.Id, clip.Text, "剪贴板", "clipboard", clip.Text, 80)).ToList();
            default:
                return [];
        }
    }

    static Hit ToolHit(string id, string title, string subtitle, string toolId, string? copy, int score) => new()
    {
        Id = id,
        Kind = "tool",
        Title = title,
        Subtitle = subtitle,
        Keywords = [],
        ToolId = toolId,
        CopyText = copy,
        Score = score
    };

    static bool LooksLikeExpression(string text) =>
        text.Any(ch => "+-*/()".Contains(ch)) && text.Any(char.IsDigit);

    static Library DefaultLibrary() => new()
    {
        Groups = [new Group { Id = "ungrouped", Name = "未分组", Color = "#98A1B0", ItemIds = [] }],
        Items = []
    };

    /// <summary>移除已废弃的侧栏虚拟分组「收藏」(id: fav)。</summary>
    static bool PurgeReservedNavGroup(Library library)
    {
        var index = library.Groups.FindIndex(group => group.Id == "fav");
        if (index < 0)
        {
            return false;
        }

        var orphaned = library.Groups[index].ItemIds;
        library.Groups.RemoveAt(index);
        if (orphaned.Count == 0)
        {
            return true;
        }

        var ungrouped = library.Groups.FindIndex(group => group.Id == "ungrouped");
        if (ungrouped < 0)
        {
            library.Groups.Add(new Group { Id = "ungrouped", Name = "未分组", Color = "#98A1B0", ItemIds = orphaned });
            return true;
        }

        foreach (var itemId in orphaned)
        {
            if (!library.Groups[ungrouped].ItemIds.Contains(itemId))
            {
                library.Groups[ungrouped].ItemIds.Add(itemId);
            }
        }

        return true;
    }

    bool PurgeReservedNavGroup() => PurgeReservedNavGroup(Library);
}

public static class Catalog
{
    public static readonly (string Id, string Title, string[] Words)[] Tools =
    [
        ("clipboard", "剪贴板", ["clip", "history", "复制"]),
        ("picker", "截图取色", ["color", "picker", "取色"]),
        ("translate", "翻译", ["fy", "translate"]),
        ("calc", "计算器", ["js", "calc", "算式"]),
        ("timestamp", "时间戳", ["ts", "time", "日期"]),
        ("unit", "单位换算", ["unit", "km", "cny"]),
        ("color", "颜色转换", ["hex", "rgb", "hsl"]),
        ("qr", "二维码", ["qr"])
    ];

    public static List<Hit> ToolHits() => Tools.Select(tool =>
    {
        var words = tool.Words.Append(tool.Title).Concat(Pinyin.Variants(tool.Title)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new Hit
        {
            Id = "tool-" + tool.Id,
            Kind = "tool",
            Title = tool.Title,
            Subtitle = "内置工具",
            Keywords = words,
            ToolId = tool.Id,
            Score = 40
        };
    }).ToList();

    public static string TitleOf(string id) => Tools.FirstOrDefault(tool => tool.Id == id).Title is string title && title.Length > 0 ? title : id;
}
