using Microsoft.Win32;

namespace Battery.App;

/// <summary>The HKCU Run entry that starts Battery at sign-in.</summary>
static class StartWithWindows
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Battery";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// When the entry exists, points it at this copy of Battery, so it still works after the folder was
    /// moved or the program renamed (Battery.App.exe became Battery.exe in 1.0).
    /// </summary>
    public static void RepointIfEnabled()
    {
        if (IsEnabled()) Set(true);
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Policy may forbid it; the menu tick then simply reflects what is really there.
        }
    }
}
