using System.IO;
using System.Text;
using Battery.Core.Providers;

namespace Battery.App;

/// <summary>
/// Connects Battery to Claude Code by pointing the status-line command in ~/.claude/settings.json at
/// Battery.StatusLine.exe. Install and Uninstall are only ever called from a user click, after a
/// confirmation that names the file. The file is backed up before every change.
/// </summary>
public static class ClaudeHookInstaller
{
    static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string SettingsPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string BackupPath => SettingsPath + ".battery-backup";

    /// <summary>The shim sits beside the running app.</summary>
    public static string ShimPath => Path.Combine(AppContext.BaseDirectory, "Battery.StatusLine.exe");

    public static bool ShimExists => File.Exists(ShimPath);

    public static bool IsInstalled()
    {
        try
        {
            return File.Exists(SettingsPath) && ClaudeSettingsEditor.IsInstalled(File.ReadAllText(SettingsPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The status-line command currently set, if any (shown in the confirmation).</summary>
    public static string? ExistingCommand()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return ClaudeSettingsEditor.Install(File.ReadAllText(SettingsPath), "x").PreviousCommand;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Throws (leaving the file untouched) when settings.json cannot be read, understood or written.</summary>
    public static void Install()
    {
        var existing = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
        var result = ClaudeSettingsEditor.Install(existing, ClaudeSettingsEditor.CommandFor(ShimPath));
        if (!result.Changed) return;

        // Record what to chain to before the switch, so the shim never runs without it.
        Directory.CreateDirectory(AppPaths.DataDir);
        if (string.IsNullOrWhiteSpace(result.PreviousCommand)) File.Delete(AppPaths.StatusLineChainFile);
        else File.WriteAllText(AppPaths.StatusLineChainFile, result.PreviousCommand, Utf8NoBom);

        WriteSettings(existing, result.Json);
    }

    /// <summary>Restores the previous status-line command (or removes ours when there was none).</summary>
    public static void Uninstall()
    {
        if (!File.Exists(SettingsPath)) return;
        var existing = File.ReadAllText(SettingsPath);
        var previous = File.Exists(AppPaths.StatusLineChainFile)
            ? File.ReadAllText(AppPaths.StatusLineChainFile).Trim()
            : null;

        var result = ClaudeSettingsEditor.Uninstall(existing, previous);
        if (!result.Changed) return;

        WriteSettings(existing, result.Json);
        File.Delete(AppPaths.StatusLineChainFile);
    }

    static void WriteSettings(string? existing, string updated)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        if (existing is not null) File.Copy(SettingsPath, BackupPath, overwrite: true);

        var tmp = SettingsPath + ".battery-tmp";
        File.WriteAllText(tmp, updated, Utf8NoBom);
        File.Move(tmp, SettingsPath, overwrite: true);
    }
}
