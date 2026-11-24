namespace Battery.Core;

/// <summary>Combines the hints of several sources that feed the same app (Claude has two).</summary>
public static class HintMerge
{
    /// <summary>
    /// While the app's reading is fresh (or it has none): null when any reported source has no hint,
    /// because that source is working; otherwise the first reported hint.
    /// When the reading is stale: the first reported hint, because a source that once worked is not
    /// working now and the other source's advice (say, "sign in") is the way to fresh numbers.
    /// Source order sets which advice is shown.
    /// </summary>
    public static string? For(IEnumerable<(bool Reported, string? Hint)> sources, bool readingIsFresh = true)
    {
        string? first = null;
        foreach (var (reported, hint) in sources)
        {
            if (!reported) continue;
            if (hint is null)
            {
                if (readingIsFresh) return null;
                continue;
            }
            first ??= hint;
        }
        return first;
    }
}
