namespace Battery.Core;

/// <summary>
/// Which apps the strip shows. The setting stores the hidden ones, so an app added later shows by default.
/// At least one app always shows: an empty strip could not be right-clicked to bring any back.
/// </summary>
public static class ShownApps
{
    /// <summary>Every app, in strip order, with its display name.</summary>
    public static IReadOnlyList<(string Id, string Name)> All { get; } = new[]
    {
        (ProviderIds.Claude, "Claude"),
        (ProviderIds.Codex, "Codex"),
        (ProviderIds.OpenCode, "OpenCode Go"),
    };

    public static bool IsShown(IEnumerable<string>? hidden, string id) => hidden is null || !hidden.Contains(id);

    /// <summary>The apps to draw: All minus the hidden ones, or All if that would leave none.</summary>
    public static IReadOnlyList<(string Id, string Name)> Visible(IEnumerable<string>? hidden)
    {
        var shown = All.Where(a => IsShown(hidden, a.Id)).ToList();
        return shown.Count == 0 ? All : shown;
    }

    /// <summary>The hidden list after flipping one app; hiding the last shown app is refused.</summary>
    public static string[] Toggle(IEnumerable<string> hidden, string id)
    {
        var list = hidden.ToList();
        if (list.Remove(id)) return list.ToArray();
        if (All.Count(a => a.Id != id && !list.Contains(a.Id)) == 0) return list.ToArray();
        list.Add(id);
        return list.ToArray();
    }
}
