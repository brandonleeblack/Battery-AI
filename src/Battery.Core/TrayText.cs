namespace Battery.Core;

/// <summary>The tray icon's tooltip: one line per shown app with 5-hour and weekly percent used.</summary>
public static class TrayText
{
    /// <summary>Windows cuts a tray tooltip at 127 characters.</summary>
    public const int MaxLength = 127;

    public static string For(IReadOnlyList<AppView> apps)
    {
        var lines = new List<string> { "Battery · used 5h / week" };
        foreach (var app in apps)
        {
            if (app.ReadAt is null)
            {
                lines.Add($"{app.DisplayName} no data");
                continue;
            }
            var old = app.FiveHour.Stale || app.Weekly.Stale ? " (old)" : "";
            lines.Add($"{app.DisplayName} {Percent(app.FiveHour)} / {Percent(app.Weekly)}{old}");
        }
        var text = string.Join("\n", lines);
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }

    static string Percent(BarView bar) =>
        bar.Missing ? "–" : Math.Round(bar.FillPercent, MidpointRounding.AwayFromZero) + "%";
}
