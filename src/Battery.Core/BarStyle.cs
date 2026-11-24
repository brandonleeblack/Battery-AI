namespace Battery.Core;

/// <summary>How bars are drawn; chosen with right-click › Bar style (docs/design/acrylic-bar-samples.html).</summary>
public enum BarStyle { Clean, Gel, Cells }

public static class BarStyles
{
    /// <summary>Menu order and names.</summary>
    public static IReadOnlyList<(BarStyle Style, string Name)> All { get; } = new[]
    {
        (BarStyle.Clean, "Clean"),
        (BarStyle.Gel, "Glass gel"),
        (BarStyle.Cells, "Cells"),
    };

    /// <summary>The saved name, any case; anything unknown is Clean.</summary>
    public static BarStyle Parse(string? name) =>
        Enum.TryParse<BarStyle>(name, ignoreCase: true, out var style) && Enum.IsDefined(style) ? style : BarStyle.Clean;
}

/// <summary>Battery-cell segments for the Cells style.</summary>
public static class BarCells
{
    public const double Gap = 1;

    /// <summary>Five cells on a strip bar, ten on the wider hover-panel bar.</summary>
    public static int CountFor(double width) => width >= 100 ? 10 : 5;

    /// <summary>Equal cells with equal gaps, spanning exactly 0..width.</summary>
    public static IReadOnlyList<(double X, double Width)> Layout(double width, int count, double gap)
    {
        var cell = (width - gap * (count - 1)) / count;
        var cells = new (double, double)[count];
        for (var i = 0; i < count; i++) cells[i] = (i * (cell + gap), cell);
        return cells;
    }
}
