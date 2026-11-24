namespace Battery.Core;

/// <summary>When the foreground window counts as a full-screen app the strip must not sit on.</summary>
public static class FullScreenRule
{
    /// <summary>
    /// Covers the whole screen, and is not merely a maximised normal window: with an auto-hide taskbar the
    /// work area is the whole monitor, so every maximised window covers it. Windows that go full screen
    /// (videos, games, slideshows) drop their caption, which a maximised normal window keeps.
    /// </summary>
    public static bool IsFullScreen(RectD window, RectD screen, bool isMaximized, bool hasCaption)
    {
        var covers = window.Left <= screen.Left && window.Top <= screen.Top
                     && window.Right >= screen.Right && window.Bottom >= screen.Bottom;
        return covers && !(isMaximized && hasCaption);
    }
}
