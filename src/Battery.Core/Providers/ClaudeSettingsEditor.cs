using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Battery.Core.Providers;

/// <summary>The settings file could not be understood, so it was left untouched.</summary>
public sealed class ClaudeSettingsFormatException : Exception
{
    public ClaudeSettingsFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Pure text-in, text-out edits of Claude Code's settings.json for the status-line hook. Only the
/// statusLine.command value is ever changed; every other setting is carried through. File handling,
/// backup and asking the user live in the app (ClaudeHookInstaller).
/// </summary>
public static class ClaudeSettingsEditor
{
    /// <summary>What marks a status-line command as Battery's.</summary>
    public const string ShimMarker = "Battery.StatusLine";

    public sealed record Result(string Json, bool Changed, string? PreviousCommand);

    static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The command to put in settings.json for the shim at <paramref name="shimExePath"/>. Claude Code
    /// runs it through Git Bash when that is installed and through PowerShell otherwise, so it must
    /// work in both: forward slashes, and no quoting that only one of them understands.
    /// </summary>
    public static string CommandFor(string shimExePath)
    {
        var path = shimExePath.Replace('\\', '/');
        return path.Contains(' ')
            ? $"powershell -NoProfile -Command \"& '{path.Replace("'", "''")}'\""
            : path;
    }

    public static bool IsInstalled(string? settingsJson)
    {
        try
        {
            return IsOurs(CurrentCommand(ParseRoot(settingsJson)));
        }
        catch (ClaudeSettingsFormatException)
        {
            return false;
        }
    }

    /// <summary>Sets statusLine.command to <paramref name="shimCommand"/> and reports what was there before.</summary>
    public static Result Install(string? settingsJson, string shimCommand)
    {
        var root = ParseRoot(settingsJson);
        var current = CurrentCommand(root);
        if (IsOurs(current)) return new Result(settingsJson ?? "", false, null);

        if (root["statusLine"] is JsonObject statusLine)
        {
            statusLine["command"] = shimCommand;
            if (statusLine["type"] is null) statusLine["type"] = "command";
        }
        else
        {
            root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = shimCommand };
        }
        return new Result(root.ToJsonString(Write), true, current);
    }

    /// <summary>
    /// Puts <paramref name="previousCommand"/> back, or removes statusLine when there was none.
    /// Does nothing when the current command is not Battery's (someone changed it since).
    /// </summary>
    public static Result Uninstall(string? settingsJson, string? previousCommand)
    {
        var root = ParseRoot(settingsJson);
        if (!IsOurs(CurrentCommand(root))) return new Result(settingsJson ?? "", false, null);

        if (string.IsNullOrWhiteSpace(previousCommand)) root.Remove("statusLine");
        else ((JsonObject)root["statusLine"]!)["command"] = previousCommand;
        return new Result(root.ToJsonString(Write), true, null);
    }

    static bool IsOurs(string? command) =>
        command is not null && command.Contains(ShimMarker, StringComparison.OrdinalIgnoreCase);

    static string? CurrentCommand(JsonObject root)
    {
        var node = root["statusLine"];
        if (node is null) return null;
        if (node is not JsonObject statusLine)
            throw new ClaudeSettingsFormatException("statusLine in settings.json is not an object.");
        return statusLine["command"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    }

    static JsonObject ParseRoot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try
        {
            return JsonNode.Parse(json.TrimStart('﻿')) as JsonObject
                ?? throw new ClaudeSettingsFormatException("settings.json does not contain a JSON object.");
        }
        catch (JsonException e)
        {
            throw new ClaudeSettingsFormatException("settings.json is not valid JSON.", e);
        }
    }
}
