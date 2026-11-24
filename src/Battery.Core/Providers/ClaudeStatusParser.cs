// Claude Code status-line input, verified on a real machine 2026-11-23 (Claude Code 2.1.331).
//
// Claude Code pipes a JSON object to the status-line command. The part Battery reads:
//
//   "rate_limits": {
//     "five_hour": { "used_percentage": 94, "resets_at": 1795503600 },   resets_at = Unix seconds
//     "seven_day": { "used_percentage": 26, "resets_at": 1795636800 }
//   }
//
// Documented (code.claude.com/docs/en/statusline.md): present only for claude.ai subscribers and only
// after the first API response; each window can be absent on its own; a window is dropped once its
// resets_at passes. An ISO 8601 resets_at is also accepted in case that changes.

using System.Text.Json;

namespace Battery.Core.Providers;

public static class ClaudeStatusParser
{
    static readonly (string Name, WindowKind Kind, TimeSpan Length)[] Windows =
    {
        ("five_hour", WindowKind.FiveHour, TimeSpan.FromHours(5)),
        ("seven_day", WindowKind.Weekly, TimeSpan.FromDays(7)),
    };

    /// <summary>Null when the text is not valid JSON or carries no usable rate_limits window.</summary>
    public static ProviderReading? Parse(string json, DateTimeOffset readAt)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json.TrimStart('﻿'));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("rate_limits", out var limits)
                || limits.ValueKind != JsonValueKind.Object)
                return null;

            var windows = new List<UsageWindow>(2);
            foreach (var (name, kind, length) in Windows)
            {
                if (limits.TryGetProperty(name, out var w)
                    && JsonTime.TryGetNumber(w, "used_percentage", out var used)
                    && w.TryGetProperty("resets_at", out var resets)
                    && JsonTime.TryRead(resets, out var resetsAt))
                {
                    windows.Add(new UsageWindow(kind, used, resetsAt, length));
                }
            }
            return windows.Count == 0 ? null : new ProviderReading(ProviderIds.Claude, windows, readAt);
        }
        catch (JsonException)
        {
            return null;   // read mid-write, or truncated
        }
    }
}
