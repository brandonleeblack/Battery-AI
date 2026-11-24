using Battery.Core;

namespace Battery.Tests;

public class ShownAppsTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    static readonly Dictionary<string, ProviderReading> NoReadings = new();
    static readonly Dictionary<string, string> NoHints = new();

    [Fact]
    public void Hidden_apps_are_left_off_and_the_order_is_kept()
    {
        var apps = StripModel.Build(NoReadings, NoHints, Now, hidden: new[] { ProviderIds.Codex });

        Assert.Equal(new[] { ProviderIds.Claude, ProviderIds.OpenCode }, apps.Select(a => a.ProviderId));
    }

    [Fact]
    public void Nothing_hidden_shows_all_three()
    {
        var apps = StripModel.Build(NoReadings, NoHints, Now, hidden: Array.Empty<string>());

        Assert.Equal(3, apps.Count);
    }

    [Fact]
    public void Unknown_ids_in_the_hidden_list_are_ignored()
    {
        var apps = StripModel.Build(NoReadings, NoHints, Now, hidden: new[] { "gemini" });

        Assert.Equal(3, apps.Count);
    }

    [Fact]
    public void Hiding_every_app_still_shows_them_all()
    {
        // A strip with nothing on it could not be right-clicked to bring anything back.
        var apps = StripModel.Build(NoReadings, NoHints, Now,
            hidden: new[] { ProviderIds.Claude, ProviderIds.Codex, ProviderIds.OpenCode });

        Assert.Equal(3, apps.Count);
    }

    [Fact]
    public void Toggle_hides_a_shown_app()
    {
        var hidden = ShownApps.Toggle(Array.Empty<string>(), ProviderIds.Codex);

        Assert.Equal(new[] { ProviderIds.Codex }, hidden);
    }

    [Fact]
    public void Toggle_shows_a_hidden_app()
    {
        var hidden = ShownApps.Toggle(new[] { ProviderIds.Codex, ProviderIds.OpenCode }, ProviderIds.Codex);

        Assert.Equal(new[] { ProviderIds.OpenCode }, hidden);
    }

    [Fact]
    public void Toggle_will_not_hide_the_last_shown_app()
    {
        var hidden = ShownApps.Toggle(new[] { ProviderIds.Codex, ProviderIds.OpenCode }, ProviderIds.Claude);

        Assert.Equal(new[] { ProviderIds.Codex, ProviderIds.OpenCode }, hidden);
    }

    [Fact]
    public void IsShown_reads_the_hidden_list()
    {
        Assert.False(ShownApps.IsShown(new[] { ProviderIds.Codex }, ProviderIds.Codex));
        Assert.True(ShownApps.IsShown(new[] { ProviderIds.Codex }, ProviderIds.Claude));
    }

    [Fact]
    public void All_lists_the_three_apps_in_strip_order_with_their_names()
    {
        Assert.Equal(new[] { ("claude", "Claude"), ("codex", "Codex"), ("opencode", "OpenCode Go") }, ShownApps.All);
    }
}
