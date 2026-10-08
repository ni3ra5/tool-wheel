using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolWheel;

/// One wheel entry: an app (.exe or .lnk), or any file, folder or URL, to open.
/// Paths may use environment variables such as %WINDIR%.
public record Tool(string Name, string? Path)
{
    [JsonIgnore] public string? Expanded => Path is null ? null : Environment.ExpandEnvironmentVariables(Path);

    public void Launch()
    {
        if (Expanded is not { } target) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { Debug.WriteLine($"Couldn't open {target}: {e.Message}"); }
    }

    /// Only checks .exe tools, by process name.
    public bool IsRunning =>
        Expanded is { } p && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(p)).Length > 0;
}

[Flags]
public enum Mods { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

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

    static Config Defaults() => new()
    {
        Wheel =
        {
            new("File Explorer", @"%WINDIR%\explorer.exe"),
            new("Edge", @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
            new("Notepad", @"%WINDIR%\System32\notepad.exe"),
            new("Calculator", @"%WINDIR%\System32\calc.exe"),
            new("Task Manager", @"%WINDIR%\System32\Taskmgr.exe"),
            new("Settings", "ms-settings:"),
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
