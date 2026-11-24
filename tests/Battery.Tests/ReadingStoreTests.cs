using Battery.Core;

namespace Battery.Tests;

public sealed class ReadingStoreTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(-7));

    readonly string _dir = Directory.CreateTempSubdirectory("battery-store-").FullName;
    string StorePath => Path.Combine(_dir, "nested", "readings.json");

    public void Dispose() => Directory.Delete(_dir, true);

    static ProviderReading Reading(string id, double used, DateTimeOffset readAt) =>
        new(id, new[]
        {
            new UsageWindow(WindowKind.FiveHour, used, Now.AddHours(2), TimeSpan.FromHours(5)),
            new UsageWindow(WindowKind.Monthly, 31.5, Now.AddDays(12), TimeSpan.FromDays(30)),
        }, readAt);

    [Fact]
    public void Put_then_reload_round_trips()
    {
        new ReadingStore(StorePath).Put(Reading(ProviderIds.Claude, 38, Now));

        var reloaded = new ReadingStore(StorePath).Current[ProviderIds.Claude];

        Assert.Equal(ProviderIds.Claude, reloaded.ProviderId);
        Assert.Equal(Now, reloaded.ReadAt);
        Assert.Equal(2, reloaded.Windows.Count);
        Assert.Equal(new UsageWindow(WindowKind.FiveHour, 38, Now.AddHours(2), TimeSpan.FromHours(5)), reloaded.Windows[0]);
        Assert.Equal(new UsageWindow(WindowKind.Monthly, 31.5, Now.AddDays(12), TimeSpan.FromDays(30)), reloaded.Windows[1]);
    }

    [Fact]
    public void Not_started_flag_round_trips()
    {
        var idle = new UsageWindow(WindowKind.FiveHour, 0, Now.AddHours(5), TimeSpan.FromHours(5)) { NotStarted = true };
        new ReadingStore(StorePath).Put(new ProviderReading(ProviderIds.OpenCode, new[] { idle }, Now));

        Assert.True(new ReadingStore(StorePath).Current[ProviderIds.OpenCode].Windows[0].NotStarted);
    }

    [Fact]
    public void Corrupt_file_loads_empty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, "{{{");

        var store = new ReadingStore(StorePath);

        Assert.Empty(store.Current);
        store.Put(Reading(ProviderIds.Codex, 5, Now));   // and it can still save over the corrupt file
        Assert.Single(new ReadingStore(StorePath).Current);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"claude\":null}")]
    [InlineData("{\"claude\":{\"ProviderId\":\"claude\",\"Windows\":null,\"ReadAt\":\"2026-11-23T12:00:00+00:00\"}}")]
    public void Wrong_shaped_file_loads_empty(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, content);

        Assert.Empty(new ReadingStore(StorePath).Current);
    }

    [Fact]
    public void Missing_file_loads_empty() => Assert.Empty(new ReadingStore(StorePath).Current);

    [Fact]
    public void Put_replaces_same_provider()
    {
        var store = new ReadingStore(StorePath);
        store.Put(Reading(ProviderIds.Claude, 38, Now));
        store.Put(Reading(ProviderIds.Codex, 10, Now));

        store.Put(Reading(ProviderIds.Claude, 40, Now.AddMinutes(1)));

        Assert.Equal(2, store.Current.Count);
        Assert.Equal(40, store.Current[ProviderIds.Claude].Windows[0].UsedPercent);
    }

    [Fact]
    public void Older_reading_does_not_replace_newer()
    {
        var store = new ReadingStore(StorePath);
        store.Put(Reading(ProviderIds.Claude, 40, Now));
        var changes = 0;
        store.Changed += () => changes++;

        store.Put(Reading(ProviderIds.Claude, 38, Now.AddMinutes(-1)));

        Assert.Equal(40, store.Current[ProviderIds.Claude].Windows[0].UsedPercent);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Put_raises_changed()
    {
        var store = new ReadingStore(StorePath);
        var changes = 0;
        store.Changed += () => changes++;

        store.Put(Reading(ProviderIds.Claude, 38, Now));

        Assert.Equal(1, changes);
    }

    [Fact]
    public void Current_is_a_snapshot_that_later_puts_do_not_mutate()
    {
        var store = new ReadingStore(StorePath);
        store.Put(Reading(ProviderIds.Claude, 38, Now));
        var snapshot = store.Current;

        store.Put(Reading(ProviderIds.Codex, 10, Now));

        Assert.Single(snapshot);
    }

    [Fact]
    public void Unwritable_path_does_not_throw_and_keeps_reading_in_memory()
    {
        // A directory where the file should be makes every save fail.
        Directory.CreateDirectory(StorePath);
        var store = new ReadingStore(StorePath);

        store.Put(Reading(ProviderIds.Claude, 38, Now));

        Assert.Single(store.Current);
    }
}
