// claude.ai account usage, verified on a real account 2026-11-24.
//
// How it is read: Battery loads https://claude.ai/settings/usage in its own WebView2 profile (signed in
// there) and captures the response to the request that page makes to
// https://claude.ai/api/organizations/<org uuid>/usage. The part Battery reads:
//
//   { "five_hour": { "utilization": 51.0, "resets_at": "2026-11-24T11:59:59.791395+00:00", ... },
//     "seven_day": { "utilization": 33.0, "resets_at": "2026-11-25T19:59:59.791417+00:00", ... },
//     "seven_day_opus": null, "seven_day_sonnet": null, ... }
//
// utilization is percent used (0-100). Many other keys are present and ignored. These figures are for
// the whole account, so Desktop, web and Claude Code use all count. Signed out, the page goes to
// /login; no signed-out JSON was available (plain HTTP clients get a Cloudflare challenge), so an
// error body is simply not recognised and the reader's 401/403 or the redirect decide "signed out".

using System.Text.Json;

namespace Battery.Core.Providers;

public enum ClaudeWebParseStatus { Ok, SignedOut, Unrecognised }

public record ClaudeWebParseResult(ClaudeWebParseStatus Status, ProviderReading? Reading);

public static class ClaudeWebUsageParser
{
    public const string UsagePageUrl = "https://claude.ai/settings/usage";

    static readonly ClaudeWebParseResult Unrecognised = new(ClaudeWebParseStatus.Unrecognised, null);

    static readonly (string Name, WindowKind Kind, TimeSpan Length)[] Windows =
    {
        ("five_hour", WindowKind.FiveHour, TimeSpan.FromHours(5)),
        ("seven_day", WindowKind.Weekly, TimeSpan.FromDays(7)),
    };

    public static ClaudeWebParseResult Parse(string payload, DateTimeOffset readAt)
    {
        if (string.IsNullOrWhiteSpace(payload)) return Unrecognised;
        try
        {
            using var doc = JsonDocument.Parse(payload.TrimStart('﻿'));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unrecognised;

            var windows = new List<UsageWindow>(2);
            foreach (var (name, kind, length) in Windows)
            {
                if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) continue;
                if (!JsonTime.TryGetNumber(w, "utilization", out var used) || used < 0) continue;
                used = Math.Min(100, used);

                if (w.TryGetProperty("resets_at", out var r) && JsonTime.TryRead(r, out var resetsAt))
                {
                    windows.Add(new UsageWindow(kind, used, resetsAt, length));
                }
                else if (used == 0)
                {
                    // Nothing used and no window running: say so rather than invent a reset time.
                    windows.Add(new UsageWindow(kind, 0, readAt + length, length) { NotStarted = true });
                }
                // Used but with no reset time: unknown clock, so leave the window out (Missing, not guessed).
            }

            return windows.Count == 0
                ? Unrecognised
                : new ClaudeWebParseResult(ClaudeWebParseStatus.Ok, new ProviderReading(ProviderIds.Claude, windows, readAt));
        }
        catch (JsonException)
        {
            return Unrecognised;   // an HTML challenge or error page, or a cut-off response
        }
    }

    /// <summary>True for the usage page's own request, /api/organizations/&lt;org&gt;/usage (any query string).</summary>
    public static bool IsUsageRequest(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !u.Host.Equals("claude.ai", StringComparison.OrdinalIgnoreCase))
            return false;
        var parts = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 && parts[0] == "api" && parts[1] == "organizations" && parts[3] == "usage";
    }

    /// <summary>True when loading the usage page ended on a sign-in page, on claude.ai or an identity provider.</summary>
    public static bool IsSignInPage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme is "http" or "https"
        && (!u.Host.EndsWith("claude.ai", StringComparison.OrdinalIgnoreCase)
            || u.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || u.AbsolutePath.StartsWith("/magic-link", StringComparison.OrdinalIgnoreCase));
}
