namespace Battery.Core;

/// <summary>Combines the hints of several sources that feed the same app (Claude has two).</summary>
public static class HintMerge
{
    /// <summary>
    /// Null when any source that has reported has no hint, because that source is working.
    /// Otherwise the first reported hint, so source order sets which advice is shown.
    /// </summary>
    public static string? For(IEnumerable<(bool Reported, string? Hint)> sources)
    {
        string? first = null;
        foreach (var (reported, hint) in sources)
        {
            if (!reported) continue;
            if (hint is null) return null;
            first ??= hint;
        }
        return first;
    }
}
