using System.IO;
using System.Text.Json;

namespace Battery.App;

/// <summary>Where Battery keeps everything it writes: %AppData%\Battery.</summary>
public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Battery");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ReadingsFile => Path.Combine(DataDir, "readings.json");
    public static string ClaudeStatusFile => Path.Combine(DataDir, "claude-status.json");
    public static string StatusLineChainFile => Path.Combine(DataDir, "statusline-chain.txt");
    public static string WebView2Dir => Path.Combine(DataDir, "WebView2");
}

public sealed class Settings
{
    /// <summary>Gap between the strip's right edge and the tray, in pixels at 100% scaling. Null = default.</summary>
    public double? OffsetFromTray { get; set; }

    public bool StartWithWindows { get; set; } = true;

    /// <summary>False until the Start-with-Windows default has been applied once.</summary>
    public bool FirstRunDone { get; set; }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load(string? path = null)
    {
        try
        {
            path ??= AppPaths.SettingsFile;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        catch
        {
            // Unreadable or corrupt settings fall back to defaults.
        }
        return new Settings();
    }

    public void Save(string? path = null)
    {
        try
        {
            path ??= AppPaths.SettingsFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Losing a saved position is not worth crashing over.
        }
    }
}
