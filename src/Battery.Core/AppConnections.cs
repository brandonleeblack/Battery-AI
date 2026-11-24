namespace Battery.Core;

/// <summary>
/// How each app is connected, worded the same way for all three: one menu item that reads "Sign in to …"
/// (or "Connect …" for Codex, which has no sign-in) until the connection is confirmed, and then only
/// "Disconnect …". Claude and OpenCode are confirmed by a successful read in Battery's own browser;
/// Codex by the user agreeing to let Battery read its session files.
/// </summary>
public sealed record AppConnection(
    string ProviderId,
    string ConnectLabel,
    string DisconnectLabel,
    string NotConnectedHint,
    string? SignedOutHint)
{
    /// <summary>The hover hint while not connected: "signed out" when the site ended Battery's session.</summary>
    public string HintWhileDisconnected(bool lapsed) =>
        lapsed && SignedOutHint is not null ? SignedOutHint : NotConnectedHint;
}

public static class AppConnections
{
    public static IReadOnlyList<AppConnection> All { get; } = new[]
    {
        WebSignIn(ProviderIds.Claude, "Claude"),
        new AppConnection(ProviderIds.Codex, "Connect Codex…", "Disconnect Codex…", "Right-click › Connect Codex", null),
        WebSignIn(ProviderIds.OpenCode, "OpenCode"),
    };

    public static AppConnection For(string providerId) => All.First(c => c.ProviderId == providerId);

    /// <summary>The saved list of connected apps with one app added or taken out.</summary>
    public static string[] WithConnected(IEnumerable<string> connected, string providerId, bool isConnected)
    {
        var list = connected.Where(id => id != providerId).ToList();
        if (isConnected) list.Add(providerId);
        return list.ToArray();
    }

    static AppConnection WebSignIn(string id, string name) => new(
        id,
        $"Sign in to {name}…",
        $"Disconnect {name}…",
        $"Right-click › Sign in to {name}",
        $"Signed out. Right-click › Sign in to {name}");
}
