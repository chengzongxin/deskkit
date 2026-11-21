using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskKit.App;

public sealed class Clip
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public bool Favorite { get; set; }
}

public sealed class Shortcut
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "file";
}

public sealed class Group
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#98A1B0";
    public List<string> ItemIds { get; set; } = [];
}

public sealed class Library
{
    public int SchemaVersion { get; set; } = 1;
    public List<Group> Groups { get; set; } = [];
    public List<Shortcut> Items { get; set; } = [];
}

public sealed class HotkeyBinding
{
    public int KeyCode { get; set; }
    public bool Alt { get; set; }
    public bool Command { get; set; }
    public bool Control { get; set; }
    public bool Shift { get; set; }
    public string Text { get; set; } = "";
}

public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;
    public bool AutoLaunch { get; set; }
    public string SummonMode { get; set; } = "hotkey";
    public bool BlurHide { get; set; } = true;
    public string MultiScreen { get; set; } = "followCursor";
    public string Theme { get; set; } = "light";
    public string Density { get; set; } = "standard";
    public string AccentToken { get; set; } = "accent";
    public string BackgroundImagePath { get; set; } = "";
    public double BackgroundOpacity { get; set; } = 1;
    public string ClipboardRetention { get; set; } = "7d";
    public bool ClipboardPaused { get; set; }
    public bool CloudSync { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public bool GuideDone { get; set; }
    public bool StarterSeeded { get; set; }
    public List<string> HiddenTools { get; set; } = [];
    public List<string> ToolOrder { get; set; } = [];
    public int SummonKeyCode { get; set; } = 49;
    public bool SummonAlt { get; set; } = true;
    public bool SummonCommand { get; set; }
    public bool SummonControl { get; set; }
    public bool SummonShift { get; set; }
    public string LastScreen { get; set; } = "";
    public bool PanelPlacedByUser { get; set; }
    public double PanelX { get; set; }
    public double PanelY { get; set; }
    public double PanelWidth { get; set; }
    public double PanelHeight { get; set; }
    public string DismissedClip { get; set; } = "";
    public Dictionary<string, HotkeyBinding> Hotkeys { get; set; } = new();
    public Dictionary<string, string> TriggerWords { get; set; } = new();
}

public sealed class Hit
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Keywords { get; set; } = [];
    public string? ToolId { get; set; }
    public string? CopyText { get; set; }
    public int Score { get; set; }
}

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static T? Read<T>(string name)
    {
        var path = DeskKit.Platform.Paths.File(name);
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch
        {
            return default;
        }
    }

    public static void Write<T>(T value, string name)
    {
        var path = DeskKit.Platform.Paths.File(name);
        var data = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, data);
        if (File.Exists(path))
        {
            File.Replace(temporary, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporary, path);
        }
    }
}
