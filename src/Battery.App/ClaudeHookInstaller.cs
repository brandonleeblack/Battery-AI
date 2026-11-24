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

    /// <summary>settings.json as it was before Battery's status line went in. Disconnect falls back to it.</summary>
    public static string BackupPath => SettingsPath + ".battery-backup";

    /// <summary>settings.json as it was just before Disconnect changed it.</summary>
    public static string UninstallBackupPath => SettingsPath + ".battery-uninstall-backup";

    /// <summary>The shim sits beside the running app (beside the real file when started through a link, as winget does).</summary>
    public static string ShimPath => Path.Combine(AppFolder(), "Battery.StatusLine.exe");

    static string AppFolder()
    {
        try
        {
            var exe = Environment.ProcessPath;
            var target = exe is null ? null : File.ResolveLinkTarget(exe, returnFinalTarget: true);
            if (target is not null) return Path.GetDirectoryName(target.FullName)!;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return AppContext.BaseDirectory;
    }

    public static bool ShimExists => File.Exists(ShimPath);

    public static bool IsInstalled()
    {
        try
        {
            return File.Exists(SettingsPath) && ClaudeSettingsEditor.IsInstalled(File.ReadAllText(SettingsPath));
        }
        catch
        {
            return false;   // the menu must open whatever state the file is in
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
        var wasOurs = ClaudeSettingsEditor.IsInstalled(existing);
        var result = ClaudeSettingsEditor.Install(existing, ClaudeSettingsEditor.CommandFor(ShimPath));
        if (!result.Changed) return;

        if (wasOurs)
        {
            // Only repointing our own command at a new path: the chain file and the pre-Battery backup stay as they are.
            WriteSettings(existing, result.Json, UninstallBackupPath);
            return;
        }

        // Record what to chain to before the switch, so the shim never runs without it.
        Directory.CreateDirectory(AppPaths.DataDir);
        if (string.IsNullOrWhiteSpace(result.PreviousCommand)) File.Delete(AppPaths.StatusLineChainFile);
        else File.WriteAllText(AppPaths.StatusLineChainFile, result.PreviousCommand, Utf8NoBom);

        WriteSettings(existing, result.Json, BackupPath);
    }

    /// <summary>Restores the previous status-line command (or removes ours when there was none).</summary>
    public static void Uninstall()
    {
        if (!File.Exists(SettingsPath)) return;
        var existing = File.ReadAllText(SettingsPath);
        // The chain file first; if %AppData%\Battery was deleted, the pre-Battery backup still knows.
        var previous = ClaudeSettingsEditor.PreviousCommand(
            File.Exists(AppPaths.StatusLineChainFile) ? File.ReadAllText(AppPaths.StatusLineChainFile) : null,
            File.Exists(BackupPath) ? File.ReadAllText(BackupPath) : null);

        var result = ClaudeSettingsEditor.Uninstall(existing, previous);
        if (!result.Changed) return;

        // A separate backup, so the pre-Battery one is never overwritten with Battery's own version.
        WriteSettings(existing, result.Json, UninstallBackupPath);
        File.Delete(AppPaths.StatusLineChainFile);
    }

    static void WriteSettings(string? existing, string updated, string backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        if (existing is not null) File.Copy(SettingsPath, backupPath, overwrite: true);

        var tmp = SettingsPath + ".battery-tmp";
        File.WriteAllText(tmp, updated, Utf8NoBom);
        File.Move(tmp, SettingsPath, overwrite: true);
    }
}
