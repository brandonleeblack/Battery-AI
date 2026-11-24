using System.Text.Json;
using Battery.Core.Providers;

namespace Battery.Tests;

public class ClaudeSettingsEditorTests
{
    const string Shim = "C:/Apps/Battery/Battery.StatusLine.exe";
    const string Existing = """
        {
          "model": "opus",
          "permissions": { "allow": ["Bash(git status)"] },
          "statusLine": {
            "type": "command",
            "command": "powershell -NoProfile -File \"C:/Users/someone/.claude/other-tool/statusline.ps1\"",
            "padding": 0
          },
          "note": "héllo & <goodbye>"
        }
        """;
    const string ExistingCommand = "powershell -NoProfile -File \"C:/Users/someone/.claude/other-tool/statusline.ps1\"";

    static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void Install_into_missing_or_empty_settings_creates_the_status_line(string? settings)
    {
        var result = ClaudeSettingsEditor.Install(settings, Shim);

        Assert.True(result.Changed);
        Assert.Null(result.PreviousCommand);
        var statusLine = Parse(result.Json).GetProperty("statusLine");
        Assert.Equal("command", statusLine.GetProperty("type").GetString());
        Assert.Equal(Shim, statusLine.GetProperty("command").GetString());
    }

    [Fact]
    public void Install_keeps_everything_else_and_reports_the_previous_command()
    {
        var result = ClaudeSettingsEditor.Install(Existing, Shim);

        Assert.True(result.Changed);
        Assert.Equal(ExistingCommand, result.PreviousCommand);
        var root = Parse(result.Json);
        Assert.Equal("opus", root.GetProperty("model").GetString());
        Assert.Equal("Bash(git status)", root.GetProperty("permissions").GetProperty("allow")[0].GetString());
        Assert.Equal("héllo & <goodbye>", root.GetProperty("note").GetString());
        Assert.Equal(Shim, root.GetProperty("statusLine").GetProperty("command").GetString());
        Assert.Equal(0, root.GetProperty("statusLine").GetProperty("padding").GetInt32());
        Assert.Contains("héllo & <goodbye>", result.Json);   // written readably, not as \u escapes
    }

    [Fact]
    public void Install_when_already_installed_changes_nothing()
    {
        var once = ClaudeSettingsEditor.Install(Existing, Shim);

        var twice = ClaudeSettingsEditor.Install(once.Json, Shim);

        Assert.False(twice.Changed);
        Assert.Equal(once.Json, twice.Json);
        Assert.Null(twice.PreviousCommand);   // must never record our own shim as the command to chain to
    }

    [Fact]
    public void Install_when_ours_at_an_old_path_moves_it_to_the_new_path_without_recording_it_as_previous()
    {
        var atOldPath = ClaudeSettingsEditor.Install(Existing, "C:/Old/Battery.StatusLine.exe").Json;

        var moved = ClaudeSettingsEditor.Install(atOldPath, Shim);

        Assert.True(moved.Changed);
        Assert.Null(moved.PreviousCommand);
        Assert.Equal(Shim, Parse(moved.Json).GetProperty("statusLine").GetProperty("command").GetString());
    }

    [Fact]
    public void Previous_command_comes_from_the_chain_file_first()
    {
        var backup = Existing;

        Assert.Equal("from-chain", ClaudeSettingsEditor.PreviousCommand("  from-chain\n", backup));
    }

    [Fact]
    public void Previous_command_falls_back_to_the_pre_battery_backup_when_the_chain_file_is_gone()
    {
        Assert.Equal(ExistingCommand, ClaudeSettingsEditor.PreviousCommand(null, Existing));
        Assert.Equal(ExistingCommand, ClaudeSettingsEditor.PreviousCommand("   ", Existing));
    }

    [Fact]
    public void Previous_command_is_never_our_own_shim()
    {
        var oursInBackup = ClaudeSettingsEditor.Install(Existing, Shim).Json;

        Assert.Null(ClaudeSettingsEditor.PreviousCommand(Shim, null));
        Assert.Null(ClaudeSettingsEditor.PreviousCommand(null, oursInBackup));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{{{")]
    [InlineData("{}")]
    public void Previous_command_is_null_when_nothing_usable_is_left(string? backup) =>
        Assert.Null(ClaudeSettingsEditor.PreviousCommand(null, backup));

    [Fact]
    public void Duplicate_keys_do_not_throw_out_of_IsInstalled() =>
        Assert.False(ClaudeSettingsEditor.IsInstalled("{\"statusLine\":{\"command\":\"a\",\"command\":\"b\"}}"));

    [Fact]
    public void IsInstalled_reflects_whose_command_is_set()
    {
        Assert.False(ClaudeSettingsEditor.IsInstalled(null));
        Assert.False(ClaudeSettingsEditor.IsInstalled("{}"));
        Assert.False(ClaudeSettingsEditor.IsInstalled("{{{"));
        Assert.False(ClaudeSettingsEditor.IsInstalled(Existing));
        Assert.True(ClaudeSettingsEditor.IsInstalled(ClaudeSettingsEditor.Install(Existing, Shim).Json));
    }

    [Fact]
    public void Uninstall_restores_the_previous_command()
    {
        var installed = ClaudeSettingsEditor.Install(Existing, Shim);

        var result = ClaudeSettingsEditor.Uninstall(installed.Json, installed.PreviousCommand);

        Assert.True(result.Changed);
        var root = Parse(result.Json);
        Assert.Equal(ExistingCommand, root.GetProperty("statusLine").GetProperty("command").GetString());
        Assert.Equal(0, root.GetProperty("statusLine").GetProperty("padding").GetInt32());
        Assert.Equal("opus", root.GetProperty("model").GetString());
    }

    [Fact]
    public void Uninstall_with_no_previous_command_removes_the_status_line()
    {
        var installed = ClaudeSettingsEditor.Install("{\"model\":\"opus\"}", Shim);

        var result = ClaudeSettingsEditor.Uninstall(installed.Json, null);

        Assert.True(result.Changed);
        var root = Parse(result.Json);
        Assert.False(root.TryGetProperty("statusLine", out _));
        Assert.Equal("opus", root.GetProperty("model").GetString());
    }

    [Fact]
    public void Uninstall_leaves_a_status_line_that_is_not_ours_alone()
    {
        var result = ClaudeSettingsEditor.Uninstall(Existing, "something-else");

        Assert.False(result.Changed);
        Assert.Equal(Existing, result.Json);
    }

    // Claude Code runs the command through Git Bash when it is installed, otherwise PowerShell,
    // so the command has to mean the same thing in both.
    [Fact]
    public void Command_for_a_path_without_spaces_is_the_bare_forward_slash_path() =>
        Assert.Equal("C:/Apps/Battery/Battery.StatusLine.exe",
            ClaudeSettingsEditor.CommandFor(@"C:\Apps\Battery\Battery.StatusLine.exe"));

    [Fact]
    public void Command_for_a_path_with_spaces_goes_through_powershell_call_operator() =>
        Assert.Equal("powershell -NoProfile -Command \"& 'C:/My Apps/Battery/Battery.StatusLine.exe'\"",
            ClaudeSettingsEditor.CommandFor(@"C:\My Apps\Battery\Battery.StatusLine.exe"));

    [Fact]
    public void Either_command_form_counts_as_installed()
    {
        var spaced = ClaudeSettingsEditor.CommandFor(@"C:\My Apps\Battery\Battery.StatusLine.exe");

        Assert.True(ClaudeSettingsEditor.IsInstalled(ClaudeSettingsEditor.Install(null, spaced).Json));
    }

    [Theory]
    [InlineData("{{{")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("{\"statusLine\":\"not an object\"}")]
    public void Settings_that_cannot_be_understood_are_refused_not_overwritten(string settings)
    {
        Assert.Throws<ClaudeSettingsFormatException>(() => ClaudeSettingsEditor.Install(settings, Shim));
        Assert.Throws<ClaudeSettingsFormatException>(() => ClaudeSettingsEditor.Uninstall(settings, null));
    }
}
