using Battery.Core;

public class DemoReadingsTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Gives_one_fresh_reading_per_app()
    {
        var readings = DemoReadings.At(Now);

        Assert.Equal(new[] { ProviderIds.Claude, ProviderIds.Codex, ProviderIds.OpenCode }.Order(), readings.Keys.Order());
        Assert.All(readings, kv => Assert.Equal(kv.Key, kv.Value.ProviderId));
        Assert.All(readings.Values, r => Assert.Equal(Now, r.ReadAt));
    }

    [Fact]
    public void Every_window_is_running_and_resets_within_its_length()
    {
        foreach (var w in DemoReadings.At(Now).Values.SelectMany(r => r.Windows))
        {
            Assert.InRange(w.UsedPercent, 0, 100);
            Assert.True(w.ResetsAt > Now);
            Assert.True(w.ResetsAt - Now <= w.Length);
        }
    }

    [Fact]
    public void Shows_each_colour_on_the_strip()
    {
        var apps = StripModel.Build(DemoReadings.At(Now), new Dictionary<string, string>(), Now);
        var states = apps.SelectMany(a => new[] { a.FiveHour, a.Weekly }).Select(b => b.State).ToHashSet();

        Assert.Contains(BarState.Normal, states);
        Assert.Contains(BarState.Ahead, states);
        Assert.Contains(BarState.Low, states);
    }
}
