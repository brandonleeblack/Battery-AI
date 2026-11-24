using Battery.Core;

namespace Battery.Tests;

public class AppConnectionsTests
{
    [Fact]
    public void Every_app_has_one_entry_in_strip_order()
    {
        Assert.Equal(ShownApps.All.Select(a => a.Id), AppConnections.All.Select(c => c.ProviderId));
    }

    [Theory]
    [InlineData("claude", "Sign in to Claude…", "Disconnect Claude…")]
    [InlineData("codex", "Connect Codex…", "Disconnect Codex…")]
    [InlineData("opencode", "Sign in to OpenCode…", "Disconnect OpenCode…")]
    public void Menu_labels(string id, string connect, string disconnect)
    {
        var c = AppConnections.For(id);
        Assert.Equal((connect, disconnect), (c.ConnectLabel, c.DisconnectLabel));
    }

    [Fact]
    public void Not_connected_hint_names_the_menu_item()
    {
        Assert.Equal("Right-click › Sign in to Claude", AppConnections.For(ProviderIds.Claude).NotConnectedHint);
        Assert.Equal("Right-click › Connect Codex", AppConnections.For(ProviderIds.Codex).NotConnectedHint);
        Assert.Equal("Right-click › Sign in to OpenCode", AppConnections.For(ProviderIds.OpenCode).NotConnectedHint);
    }

    [Fact]
    public void Signed_out_hint_for_web_sign_ins()
    {
        Assert.Equal("Signed out. Right-click › Sign in to Claude", AppConnections.For(ProviderIds.Claude).SignedOutHint);
        Assert.Equal("Signed out. Right-click › Sign in to OpenCode", AppConnections.For(ProviderIds.OpenCode).SignedOutHint);
    }

    [Fact]
    public void Hint_while_not_connected_says_signed_out_only_when_a_sign_in_lapsed()
    {
        var claude = AppConnections.For(ProviderIds.Claude);
        Assert.Equal(claude.NotConnectedHint, claude.HintWhileDisconnected(lapsed: false));
        Assert.Equal(claude.SignedOutHint, claude.HintWhileDisconnected(lapsed: true));

        // Codex has no sign-in to lapse.
        var codex = AppConnections.For(ProviderIds.Codex);
        Assert.Equal(codex.NotConnectedHint, codex.HintWhileDisconnected(lapsed: true));
    }

    [Fact]
    public void Connected_list_toggles()
    {
        var list = AppConnections.WithConnected(Array.Empty<string>(), ProviderIds.Codex, true);
        Assert.Equal(new[] { ProviderIds.Codex }, list);
        Assert.Equal(new[] { ProviderIds.Codex }, AppConnections.WithConnected(list, ProviderIds.Codex, true));
        Assert.Empty(AppConnections.WithConnected(list, ProviderIds.Codex, false));
    }
}
