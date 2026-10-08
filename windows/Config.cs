using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolWheel;

/// One wheel entry: an app (.exe, .lnk or shell:AppsFolder\<app ID>), or any file, folder or URL, to open.
/// Paths may use environment variables such as %WINDIR%.
public record Tool(string Name, string? Path)
{
    [JsonIgnore] public string? Expanded => Path is null ? null : Environment.ExpandEnvironmentVariables(Path);

    /// Same thing to open, however the path is written.
    public bool Same(Tool other) => string.Equals(Expanded, other.Expanded, StringComparison.OrdinalIgnoreCase);
}

[Flags]
public enum Mods { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

static class Keys
{
    public static string Describe(Mods m) => string.Join(" + ",
        new[] { (Mods.Ctrl, "Ctrl"), (Mods.Alt, "Alt"), (Mods.Shift, "Shift"), (Mods.Win, "Win") }
            .Where(k => m.HasFlag(k.Item1)).Select(k => k.Item2));

    public static int Count(Mods m) => System.Numerics.BitOperations.PopCount((uint)m);
}

/// Same shape as the Mac app's tools.json; `shortcut` holds <see cref="Mods"/> flags here.
public class Config
{
    public List<Tool> Wheel { get; set; } = new();
    public int? Shortcut { get; set; }
    public bool? ReleaseToOpen { get; set; }

    public static readonly Mods DefaultTrigger = Mods.Ctrl | Mods.Alt | Mods.Win;  // like ⌃⌥⌘ on the Mac
    [JsonIgnore] public Mods Trigger => Shortcut is int s ? (Mods)s : DefaultTrigger;
}

static class Store
{
    public static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToolWheel", "tools.json");

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// Notepad, Calculator and Settings are Store apps on Windows 11; the old .exe names only hand off to them.
    static Tool StoreApp(string name, string appId, string fallback) =>
        new(name, Native.Exists(Apps.Folder + appId) ? Apps.Folder + appId : fallback);

    static Config Defaults() => new()
    {
        Wheel =
        {
            new("File Explorer", @"%WINDIR%\explorer.exe"),
            new("Edge", @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
            StoreApp("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", @"%WINDIR%\System32\notepad.exe"),
            StoreApp("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", @"%WINDIR%\System32\calc.exe"),
            new("Task Manager", @"%WINDIR%\System32\Taskmgr.exe"),
            StoreApp("Settings", "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", "ms-settings:"),
        },
    };

    /// Re-read on every open, so edits apply without restarting.
    public static Config Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var fresh = Defaults();
            Save(fresh);
            return fresh;
        }
        try
        {
            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath), Json) ?? Defaults();
            config.Wheel = config.Wheel.Where(t => t.Path is not null).ToList();
            return config;
        }
        catch (JsonException)
        {
            // Unreadable: set it aside rather than let the next save overwrite it.
            File.Move(ConfigPath, ConfigPath + ".invalid", overwrite: true);
            return Defaults();
        }
    }

    public static void Save(Config config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Json));
    }
}
