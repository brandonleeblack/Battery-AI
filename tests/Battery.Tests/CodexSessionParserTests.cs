using Battery.Core;
using Battery.Core.Providers;

namespace Battery.Tests;

public class CodexSessionParserTests
{
    static readonly DateTimeOffset Fallback = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    static string Sample(string timestamp, string primary, string secondary) =>
        "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null," +
        "\"rate_limits\":{\"limit_id\":\"codex\",\"primary\":" + primary + ",\"secondary\":" + secondary + "}}}";

    static string Window(double used, int minutes, long resetsAt) =>
        $"{{\"used_percent\":{used},\"window_minutes\":{minutes},\"resets_at\":{resetsAt}}}";

    [Fact]
    public void Parses_fixture()
    {
        var reading = CodexSessionParser.ParseLastSample(Fixture.Lines("codex-rollout.jsonl"), Fallback);

        Assert.NotNull(reading);
        Assert.Equal(ProviderIds.Codex, reading.ProviderId);
        Assert.Equal(DateTimeOffset.Parse("2026-11-24T05:35:46.223Z"), reading.ReadAt);
        Assert.Equal(2, reading.Windows.Count);
        var five = reading.Windows.Single(w => w.Kind == WindowKind.FiveHour);
        Assert.Equal(99, five.UsedPercent);
        Assert.Equal(TimeSpan.FromMinutes(300), five.Length);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795511294), five.ResetsAt);
        var week = reading.Windows.Single(w => w.Kind == WindowKind.Weekly);
        Assert.Equal(27, week.UsedPercent);
        Assert.Equal(TimeSpan.FromMinutes(10080), week.Length);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1796044828), week.ResetsAt);
    }

    [Fact]
    public void Newest_sample_wins()
    {
        var lines = new[]
        {
            Sample("2026-11-23T10:00:00.000Z", Window(10, 300, 1795500000), Window(5, 10080, 1796000000)),
            Sample("2026-11-23T11:00:00.000Z", Window(40, 300, 1795500000), Window(9, 10080, 1796000000)),
        };

        var reading = CodexSessionParser.ParseLastSample(lines, Fallback);

        Assert.Equal(40, reading!.Windows.Single(w => w.Kind == WindowKind.FiveHour).UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-11-23T11:00:00Z"), reading.ReadAt);
    }

    [Fact]
    public void Lines_without_rate_limits_are_skipped()
    {
        var lines = new[]
        {
            "{\"timestamp\":\"2026-11-23T10:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{}}",
            "{\"timestamp\":\"2026-11-23T10:00:01.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null}}",
            "",
        };

        Assert.Null(CodexSessionParser.ParseLastSample(lines, Fallback));
    }

    [Fact]
    public void Only_weekly_window_present_returns_one_window()
    {
        var lines = new[] { Sample("2026-11-23T10:00:00.000Z", "null", Window(9, 10080, 1796000000)) };

        var reading = CodexSessionParser.ParseLastSample(lines, Fallback);

        Assert.Equal(WindowKind.Weekly, Assert.Single(reading!.Windows).Kind);
    }

    [Fact]
    public void Weekly_as_primary_is_still_weekly()
    {
        var lines = new[] { Sample("2026-11-23T10:00:00.000Z", Window(9, 10080, 1796000000), "null") };

        var reading = CodexSessionParser.ParseLastSample(lines, Fallback);

        Assert.Equal(WindowKind.Weekly, Assert.Single(reading!.Windows).Kind);
    }

    [Fact]
    public void Half_written_last_line_is_ignored()
    {
        var real = Fixture.Lines("codex-rollout.jsonl");
        var lastSample = real.Last(l => l.Contains("\"limit_id\":\"codex\""));
        var lines = real.Append(lastSample[..(lastSample.Length / 2)]);

        var reading = CodexSessionParser.ParseLastSample(lines, Fallback);

        Assert.Equal(99, reading!.Windows.Single(w => w.Kind == WindowKind.FiveHour).UsedPercent);
    }

    [Fact]
    public void Unknown_window_length_is_dropped()
    {
        var lines = new[] { Sample("2026-11-23T10:00:00.000Z", Window(50, 1440, 1795500000), Window(9, 10080, 1796000000)) };

        var reading = CodexSessionParser.ParseLastSample(lines, Fallback);

        Assert.Equal(WindowKind.Weekly, Assert.Single(reading!.Windows).Kind);
    }

    [Fact]
    public void Sample_with_no_windows_does_not_hide_an_earlier_one()
    {
        // Seen for real: a later token_count for another limit ("premium") carries primary and secondary null.
        var lines = new[]
        {
            Sample("2026-11-23T10:00:00.000Z", Window(40, 300, 1795500000), Window(9, 10080, 1796000000)),
            Sample("2026-11-23T10:00:01.000Z", "null", "null"),
        };

        var reading = CodexSessionParser.ParseLastSample(lines, Fallback);

        Assert.Equal(2, reading!.Windows.Count);
    }

    [Fact]
    public void Missing_timestamp_uses_fallback_read_time()
    {
        var line = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":" +
                   Window(40, 300, 1795500000) + ",\"secondary\":null}}}";

        var reading = CodexSessionParser.ParseLastSample(new[] { line }, Fallback);

        Assert.Equal(Fallback, reading!.ReadAt);
    }

    [Fact]
    public void Empty_directory_returns_null()
    {
        var dir = Directory.CreateTempSubdirectory("battery-codex-").FullName;
        try
        {
            Assert.Null(CodexSessionParser.LatestReading(dir, Fallback));
            Assert.Null(CodexSessionParser.LatestReading(Path.Combine(dir, "does-not-exist"), Fallback));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Latest_reading_is_newest_sample_across_files_even_while_one_is_open_for_writing()
    {
        var dir = Directory.CreateTempSubdirectory("battery-codex-").FullName;
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(dir, "2026", "11", "23")).FullName;
            File.WriteAllLines(Path.Combine(nested, "rollout-a.jsonl"),
                new[] { Sample("2026-11-23T11:00:00.000Z", Window(70, 300, 1795500000), "null") });
            var b = Path.Combine(nested, "rollout-b.jsonl");
            File.WriteAllLines(b, new[] { Sample("2026-11-23T10:00:00.000Z", Window(20, 300, 1795500000), "null") });
            File.WriteAllText(Path.Combine(nested, "notes.jsonl"),
                Sample("2026-11-23T12:00:00.000Z", Window(1, 300, 1795500000), "null"));

            using (new FileStream(b, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                var reading = CodexSessionParser.LatestReading(dir, Fallback);

                Assert.Equal(70, Assert.Single(reading!.Windows).UsedPercent);
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
