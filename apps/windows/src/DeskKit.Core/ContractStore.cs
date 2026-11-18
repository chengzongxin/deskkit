using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeskKit.Core;

public sealed class ContractException : Exception
{
    public ContractException(string message) : base(message) { }
}

public sealed class TriggerDef
{
    public string Word { get; init; } = "";
    public string ToolId { get; init; } = "";
    public string Label { get; init; } = "";
    public string Placeholder { get; init; } = "";
}

public sealed class PermissionDef
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Reason { get; init; } = "";
}

public sealed class StagedDiff
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Owner { get; init; } = "";
    public string DueAt { get; init; } = "";
    public string ParityPlan { get; init; } = "";
}

/// <summary>
/// 读取 contracts/ 里的共享 JSON。色值、尺寸、文案、状态机都从这里来，界面不要自己写死。
/// </summary>
public sealed class ContractStore
{
    static readonly string[] Required =
    [
        "contract-version.json", "tokens.json", "layout.json", "ui-state.json", "triggers.json",
        "keymap.json", "i18n.json", "permissions.json", "platform-diffs.json",
        "schema/index-item.json", "schema/groups.json", "schema/clipboard.json", "schema/settings.json"
    ];

    readonly Dictionary<string, JsonElement> files = new(StringComparer.Ordinal);

    public string Root { get; }
    public string Hash { get; }

    public ContractStore(string root)
    {
        Root = root;
        foreach (var relative in Required)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new ContractException($"缺少契约文件 {relative}");
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            files[relative] = doc.RootElement.Clone();
        }

        if (files["contract-version.json"].GetProperty("contractVersion").GetString() != "1")
        {
            throw new ContractException("contractVersion 必须是 1");
        }

        foreach (var item in files["platform-diffs.json"].GetProperty("staged").EnumerateArray())
        {
            foreach (var field in new[] { "owner", "dueAt", "parityPlan" })
            {
                if (string.IsNullOrEmpty(Str(item, field)))
                {
                    throw new ContractException($"阶段性差异缺少 {field}");
                }
            }
        }

        Hash = HashTree(root);
    }

    public static string? Locate(string? start = null)
    {
        var env = Environment.GetEnvironmentVariable("DESKKIT_CONTRACTS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        var seeds = new List<string>();
        if (!string.IsNullOrWhiteSpace(start))
        {
            seeds.Add(start);
        }

        seeds.Add(AppContext.BaseDirectory);
        seeds.Add(Environment.CurrentDirectory);
        foreach (var seed in seeds)
        {
            var cursor = seed;
            for (var i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(cursor, "contract-version.json")))
                {
                    return cursor;
                }

                var nested = Path.Combine(cursor, "contracts");
                if (File.Exists(Path.Combine(nested, "contract-version.json")))
                {
                    return nested;
                }

                var parent = Path.GetDirectoryName(cursor.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(parent) || parent == cursor)
                {
                    break;
                }

                cursor = parent;
            }
        }

        return null;
    }

    public string Text(string key)
    {
        if (files["i18n.json"].GetProperty("strings").TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? key;
        }

        return key;
    }

    public string Text(string key, IReadOnlyDictionary<string, string> values)
    {
        var output = Text(key);
        foreach (var pair in values)
        {
            output = output.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        return output;
    }

    public string Color(string name, bool dark = false)
    {
        if (dark
            && files["tokens.json"].TryGetProperty("dark", out var darks)
            && darks.TryGetProperty(name, out var darkValue)
            && darkValue.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(darkValue.GetString()))
        {
            return darkValue.GetString()!;
        }

        if (files["tokens.json"].GetProperty("color").TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "#000000";
        }

        return "#000000";
    }

    public IReadOnlyList<string> GroupColors =>
        files["tokens.json"].GetProperty("chip.group").EnumerateArray().Select(item => item.GetString() ?? "").ToArray();

    public double Layout(params string[] path)
    {
        JsonElement node = files["layout.json"];
        foreach (var key in path)
        {
            if (!node.TryGetProperty(key, out node))
            {
                return 0;
            }
        }

        return node.ValueKind == JsonValueKind.Number ? node.GetDouble() : 0;
    }

    public string KeyLabel(string platform, string key)
    {
        if (files["keymap.json"].GetProperty("keys").TryGetProperty(platform, out var table)
            && table.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? key.ToUpperInvariant();
        }

        return key.ToUpperInvariant();
    }

    public IReadOnlyList<string> ChordParts(string action)
    {
        if (!files["keymap.json"].GetProperty("chords").TryGetProperty(action, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
    }

    public string FormatChord(string platform, string action) =>
        string.Join("+", ChordParts(action).Select(part => KeyLabel(platform, part)));

    public string CompactChord(string platform, string action)
    {
        var parts = ChordParts(action).Select(part => KeyLabel(platform, part));
        return platform == "macos" ? string.Concat(parts) : string.Join("+", parts);
    }

    public IReadOnlyList<TriggerDef> Triggers =>
        files["triggers.json"].GetProperty("triggers").EnumerateArray().Select(item => new TriggerDef
        {
            Word = Str(item, "word"),
            ToolId = Str(item, "toolId"),
            Label = Str(item, "label"),
            Placeholder = Str(item, "placeholder")
        }).ToArray();

    public (string ToolId, string Label, string Placeholder)? Trigger(string word)
    {
        foreach (var item in Triggers)
        {
            if (string.Equals(item.Word, word, StringComparison.OrdinalIgnoreCase))
            {
                return (item.ToolId, item.Label, item.Placeholder);
            }
        }

        return null;
    }

    public IReadOnlyList<PermissionDef> Permissions(string platform)
    {
        if (!files["permissions.json"].GetProperty("platforms").TryGetProperty(platform, out var list))
        {
            return Array.Empty<PermissionDef>();
        }

        return list.EnumerateArray().Select(item => new PermissionDef
        {
            Id = Str(item, "id"),
            Title = Str(item, "title"),
            Reason = Str(item, "reason")
        }).ToArray();
    }

    public IReadOnlyList<StagedDiff> StagedDiffs =>
        files["platform-diffs.json"].GetProperty("staged").EnumerateArray().Select(item => new StagedDiff
        {
            Id = Str(item, "id"),
            Category = Str(item, "category"),
            Owner = Str(item, "owner"),
            DueAt = Str(item, "dueAt"),
            ParityPlan = Str(item, "parityPlan")
        }).ToArray();

    public IReadOnlyDictionary<string, string[]> Transitions() => Map("ui-state.json", "transitions");

    public IReadOnlyDictionary<string, string[]> EscMap() => Map("ui-state.json", "esc");

    public JsonElement Fixture(string name)
    {
        var path = Path.Combine(Root, "fixtures", name);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    public static string HashTree(string root)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(relative => !relative.EndsWith(".DS_Store", StringComparison.Ordinal))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .ToArray();

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relative in files)
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            hasher.AppendData(Encoding.UTF8.GetBytes(relative));
            hasher.AppendData(new byte[] { 0 });
            hasher.AppendData(File.ReadAllBytes(full));
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    Dictionary<string, string[]> Map(string file, string key)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var property in files[file].GetProperty(key).EnumerateObject())
        {
            result[property.Name] = property.Value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
        }

        return result;
    }

    static string Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
