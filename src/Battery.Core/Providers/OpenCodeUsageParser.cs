// OpenCode Go usage, verified on a real account 2026-11-23.
//
// How it is read: Battery loads https://opencode.ai/console in its own WebView2 profile (signed in
// there) and captures the response to the request the console page itself makes to
// https://opencode.ai/console/api/go/status. Requesting that URL directly answers HTTP 400; the
// endpoint needs what the console's own request carries. Signed in, the response is JSON like:
//
//   { "product": "go",
//     "access": { "startsAt": "2026-11-17T17:55:26.000Z", "endsAt": "2026-12-17T17:55:26.000Z",
//       "meters": {
//         "fiveHour": { "startsAt": null, "resetsAt": null, "limitMicroCents": "1200000000", "usedMicroCents": "0" },
//         "week":     { "startsAt": "2026-11-23T00:00:00.000Z", "resetsAt": "2026-11-30T00:00:00.000Z",
//                       "limitMicroCents": "3000000000", "usedMicroCents": "2413179" },
//         "month":    { "resetsAt": "2026-12-17T17:55:26.000Z", "limitMicroCents": "6000000000", "usedMicroCents": "3002413179" } } } }
//
// Amounts are strings of micro-cents; percent used = used / limit x 100, capped at 100 (Go can spend
// credit past a limit). fiveHour has null times while no 5-hour window is running. The billing month
// runs access.startsAt to access.endsAt. Signed out, the same URL answers 401 {"_tag":"Unauthorized"}.

using System.Globalization;
using System.Text.Json;

namespace Battery.Core.Providers;

public enum OpenCodeParseStatus { Ok, SignedOut, Unrecognised }

public record OpenCodeParseResult(OpenCodeParseStatus Status, ProviderReading? Reading);

public static class OpenCodeUsageParser
{
    public const string ConsoleUrl = "https://opencode.ai/console";
    public const string UsageUrl = "https://opencode.ai/console/api/go/status";
    const string UsagePath = "/console/api/go/status";

    /// <summary>True for the console's own request for Go usage (any query string).</summary>
    public static bool IsUsageRequest(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Host.Equals("opencode.ai", StringComparison.OrdinalIgnoreCase)
        && u.AbsolutePath.Equals(UsagePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when loading the console ended on a sign-in page, here or at an identity provider.</summary>
    public static bool IsSignInPage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme is "http" or "https"
        && (!u.Host.EndsWith("opencode.ai", StringComparison.OrdinalIgnoreCase)
            || u.AbsolutePath.Contains("/login", StringComparison.OrdinalIgnoreCase)
            || u.AbsolutePath.StartsWith("/auth", StringComparison.OrdinalIgnoreCase));

    static readonly OpenCodeParseResult Unrecognised = new(OpenCodeParseStatus.Unrecognised, null);
    static readonly OpenCodeParseResult SignedOut = new(OpenCodeParseStatus.SignedOut, null);

    public static OpenCodeParseResult Parse(string payload, DateTimeOffset readAt)
    {
        if (string.IsNullOrWhiteSpace(payload)) return Unrecognised;
        try
        {
            using var doc = JsonDocument.Parse(payload.TrimStart('﻿'));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unrecognised;

            if (root.TryGetProperty("_tag", out var tag) && tag.ValueKind == JsonValueKind.String
                && tag.GetString() is "Unauthorized" or "Unauthenticated")
                return SignedOut;

            if (!root.TryGetProperty("access", out var access) || access.ValueKind != JsonValueKind.Object
                || !access.TryGetProperty("meters", out var meters) || meters.ValueKind != JsonValueKind.Object)
                return Unrecognised;

            var periodStart = Time(access, "startsAt");
            var windows = new List<UsageWindow>(3);

            if (Meter(meters, "fiveHour", out var five))
            {
                // No 5-hour window running: nothing used in one, and a new one would start now.
                var resetsAt = five.ResetsAt ?? readAt.AddHours(5);
                windows.Add(new UsageWindow(WindowKind.FiveHour, five.Used, resetsAt,
                    LengthOr(five.StartsAt, five.ResetsAt, TimeSpan.FromHours(5)))
                {
                    NotStarted = five.ResetsAt is null,
                });
            }
            if (Meter(meters, "week", out var week) && week.ResetsAt is { } weekReset)
            {
                windows.Add(new UsageWindow(WindowKind.Weekly, week.Used, weekReset,
                    LengthOr(week.StartsAt, weekReset, TimeSpan.FromDays(7))));
            }
            if (Meter(meters, "month", out var month) && month.ResetsAt is { } monthReset)
            {
                var start = month.StartsAt ?? periodStart;
                var length = start is { } s && monthReset > s
                    ? monthReset - s
                    : TimeSpan.FromDays(DateTime.DaysInMonth(monthReset.Year, monthReset.Month == 1 ? 12 : monthReset.Month - 1));
                windows.Add(new UsageWindow(WindowKind.Monthly, month.Used, monthReset, length));
            }

            return windows.Count == 0
                ? Unrecognised
                : new OpenCodeParseResult(OpenCodeParseStatus.Ok, new ProviderReading(ProviderIds.OpenCode, windows, readAt));
        }
        catch (JsonException)
        {
            return Unrecognised;   // an HTML sign-in page, an error page, or a cut-off response
        }
    }

    readonly record struct MeterValue(double Used, DateTimeOffset? StartsAt, DateTimeOffset? ResetsAt);

    static bool Meter(JsonElement meters, string name, out MeterValue value)
    {
        value = default;
        if (!meters.TryGetProperty(name, out var m) || m.ValueKind != JsonValueKind.Object) return false;
        if (!Amount(m, "limitMicroCents", out var limit) || limit <= 0) return false;
        if (!Amount(m, "usedMicroCents", out var used) || used < 0) return false;
        value = new MeterValue(Math.Min(100, used / limit * 100), Time(m, "startsAt"), Time(m, "resetsAt"));
        return true;
    }

    static TimeSpan LengthOr(DateTimeOffset? start, DateTimeOffset? end, TimeSpan fallback) =>
        start is { } s && end is { } e && e > s ? e - s : fallback;

    static DateTimeOffset? Time(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && JsonTime.TryRead(p, out var t) ? t : null;

    /// <summary>Micro-cent amounts arrive as strings; a plain JSON number is accepted too.</summary>
    static bool Amount(JsonElement obj, string name, out double value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var p)) return false;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetDouble(out value) && double.IsFinite(value),
            JsonValueKind.String => double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                                    && double.IsFinite(value),
            _ => false,
        };
    }
}
