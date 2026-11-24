// Codex CLI session files, verified on a real machine 2026-11-23.
//
// Location: %USERPROFILE%\.codex\sessions\YYYY\MM\DD\rollout-<time>-<uuid>.jsonl, one JSON object per line.
// Lines carrying rate limits look like:
//
//   { "timestamp": "2026-11-24T05:35:46.223Z",            ISO 8601, UTC
//     "type": "event_msg",
//     "payload": { "type": "token_count",
//       "rate_limits": {
//         "limit_id": "codex",
//         "primary":   { "used_percent": 99.0, "window_minutes": 300,   "resets_at": 1795511294 },
//         "secondary": { "used_percent": 27.0, "window_minutes": 10080, "resets_at": 1796044828 },
//         ... } } }
//
// resets_at is Unix seconds. primary and secondary can both be null (seen on a later line with
// "limit_id": "premium"); such a line carries no window and must not hide an earlier real sample.
// Windows are classified by window_minutes, not by primary/secondary position.

using System.Text;
using System.Text.Json;

namespace Battery.Core.Providers;

public static class CodexSessionParser
{
    const string Marker = "\"rate_limits\"";
    const int FilesToScan = 5;
    const long TailBytes = 2 * 1024 * 1024;

    /// <summary>
    /// The last line that carries at least one recognised window. ReadAt is that line's own timestamp,
    /// or <paramref name="fallbackReadAt"/> when it has none. Unparseable lines are skipped.
    /// </summary>
    public static ProviderReading? ParseLastSample(IEnumerable<string> jsonlLines, DateTimeOffset fallbackReadAt)
    {
        ProviderReading? last = null;
        foreach (var line in jsonlLines)
        {
            if (line is null || !line.Contains(Marker, StringComparison.Ordinal)) continue;
            var reading = ParseLine(line, fallbackReadAt);
            if (reading is not null) last = reading;
        }
        return last;
    }

    /// <summary>
    /// Newest sample across the most recently modified rollout files under <paramref name="sessionsDir"/>.
    /// Never throws for missing, locked or half-written files.
    /// </summary>
    public static ProviderReading? LatestReading(string sessionsDir, DateTimeOffset now)
    {
        List<FileInfo> files;
        try
        {
            if (!Directory.Exists(sessionsDir)) return null;
            files = new DirectoryInfo(sessionsDir)
                .EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(FilesToScan)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        ProviderReading? newest = null;
        foreach (var file in files)
        {
            var reading = ReadFile(file.FullName, now);
            if (reading is not null && (newest is null || reading.ReadAt > newest.ReadAt)) newest = reading;
        }
        return newest;
    }

    static ProviderReading? ReadFile(string path, DateTimeOffset now)
    {
        try
        {
            // Codex may be appending right now, so share the file for writing.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > TailBytes)
            {
                // Session files reach tens of megabytes; the newest sample is near the end.
                stream.Seek(-TailBytes, SeekOrigin.End);
                var fromTail = ParseLastSample(ReadLines(stream, skipFirst: true), now);
                if (fromTail is not null) return fromTail;
                stream.Seek(0, SeekOrigin.Begin);
            }
            return ParseLastSample(ReadLines(stream, skipFirst: false), now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static IEnumerable<string> ReadLines(Stream stream, bool skipFirst)
    {
        var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16, leaveOpen: true);
        if (skipFirst) reader.ReadLine();   // we landed mid-line
        while (reader.ReadLine() is { } line) yield return line;
    }

    static ProviderReading? ParseLine(string line, DateTimeOffset fallbackReadAt)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
                return null;

            var windows = new List<UsageWindow>(2);
            foreach (var name in new[] { "primary", "secondary" })
            {
                if (limits.TryGetProperty(name, out var w) && TryWindow(w, out var window)
                    && windows.All(x => x.Kind != window.Kind))
                    windows.Add(window);
            }
            if (windows.Count == 0) return null;

            var readAt = root.TryGetProperty("timestamp", out var ts) && JsonTime.TryRead(ts, out var t) ? t : fallbackReadAt;
            return new ProviderReading(ProviderIds.Codex, windows.OrderBy(w => w.Kind).ToList(), readAt);
        }
        catch (JsonException)
        {
            return null;   // half-written or truncated line
        }
    }

    static bool TryWindow(JsonElement w, out UsageWindow window)
    {
        window = null!;
        if (!JsonTime.TryGetNumber(w, "used_percent", out var used)
            || !JsonTime.TryGetNumber(w, "window_minutes", out var minutes)
            || !w.TryGetProperty("resets_at", out var resets) || !JsonTime.TryRead(resets, out var resetsAt))
            return false;

        WindowKind kind;
        if (minutes == 300) kind = WindowKind.FiveHour;
        else if (minutes == 10080) kind = WindowKind.Weekly;
        else return false;

        window = new UsageWindow(kind, used, resetsAt, TimeSpan.FromMinutes(minutes));
        return true;
    }
}
