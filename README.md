# Battery

A small strip that floats on the Windows taskbar, just left of the system tray, showing how much of
three AI subscriptions you have used: **Claude**, **Codex** and **OpenCode Go**, in that order.

Each app has a coloured dot and two bars: the **5-hour** window on top and the **weekly** window below.
A bar fills with the percent **used**. A thin line on each bar shows how much of that window's time
has gone by, so you can see at a glance whether you are ahead of the clock.

- **Grey:** fine.
- **Amber:** used is more than 5 points ahead of the clock line.
- **Red:** less than 20% left. Red wins over amber.
- **Dimmed:** the reading is more than 15 minutes old.
- **Empty outline:** never read yet. Hover to see what to do.

Hover over the strip for every window (including OpenCode's monthly one), percent used, time to reset
and how old each reading is. Right-click for **Refresh now**, **Sign in to Claude…**, **Sign in to OpenCode…**,
**Connect / Disconnect Claude Code**, **Show** (tick which apps appear; a hidden app is not read, and
its sign-in items leave the menu), **Start with Windows** and **Quit**. Drag the strip sideways to
move it along the taskbar; the position is remembered.

The colours follow the Windows mode that colours the taskbar (Settings › Personalisation › Colours ›
"Choose your default Windows mode"): Graphite when dark, Daylight when light, switching live. The
strip hides while a full-screen app is in front and while an auto-hide taskbar is tucked away.

## Build and run

Needs Windows 10 or 11 and the .NET SDK 8 or later.

```powershell
.\publish.ps1
```

This writes two self-contained executables to `dist\`:

- `dist\Battery.App.exe`: the strip. Run this one.
- `dist\Battery.StatusLine.exe`: the Claude Code helper. It must stay in the same folder.

Move the whole `dist` folder wherever you like before choosing Start with Windows or Connect Claude
Code, because both remember the path. Tests: `dotnet test`.

## Where the numbers come from

All three sources are unofficial and may change without notice. Each provider is isolated, so one
breaking does not affect the others; it just shows a hint in the hover panel.

### Claude: right-click › Sign in to Claude… (and optionally Connect Claude Code…)

Claude has two sources. Battery shows whichever reading is newer.

**claude.ai (recommended).** **Sign in to Claude…** opens claude.ai in a browser window that uses
Battery's own private profile (`%AppData%\Battery\WebView2`). Sign in there once and close it; email
sign-in works more reliably there than Google. Every 5 minutes Battery loads Settings › Usage in a
hidden copy of that browser and reads the 5-hour and weekly figures the page fetches
(`/api/organizations/<id>/usage`). These are for your whole account, so use from Claude Desktop, the
web and Claude Code all counts. Battery never sees your password and never reads the sign-in stored by
Claude Desktop or Claude Code. If the session expires, the bars dim and the hover says to sign in again.

**Claude Code status line (optional, faster while Claude Code runs).**

Claude Code passes a JSON blob to its status-line command on every update; it includes
`rate_limits.five_hour` and `rate_limits.seven_day`, each with `used_percentage` and `resets_at`.
**Connect Claude Code…** points the status line in `%USERPROFILE%\.claude\settings.json` at
`Battery.StatusLine.exe`, after showing exactly what will change. It first backs the file up to
`settings.json.battery-backup`. If you already had a status-line command, Battery runs it after
saving its reading, so your own status line keeps working. **Disconnect Claude Code** puts the old
command back. It first backs the current file up to `settings.json.battery-uninstall-backup`, so the
pre-Battery backup is never overwritten. If `%AppData%\Battery` was deleted, the old command is
recovered from that pre-Battery backup. If you move the app folder, choose **Connect Claude Code…**
again and the command is repointed.

Limits:

- Readings refresh only while Claude Code is running. Claude Desktop uses the same allowance, so
  Desktop-only use does not show until Claude Code next runs. Old readings dim honestly.
- `rate_limits` is only sent to claude.ai subscribers, and only after the first reply in a session.
- If a Claude Code version drops `rate_limits`, that counts as "no reading".
- Battery does not read Claude Code's stored login, so it cannot ask Anthropic directly.

### Codex: nothing to connect

Codex CLI writes session files under `%USERPROFILE%\.codex\sessions`. Its `token_count` lines carry
rate-limit samples (used percent, window length, reset time), and Battery reads the newest one every
minute.

Limits:

- Fresh only when Codex has run recently.
- OpenAI has added and removed the 5-hour window during 2026; if a window is absent, its bar shows
  as missing, not zero.
- Battery does not read Codex's stored login.

### OpenCode Go: right-click › Sign in to OpenCode…

There is no API for Go limits, so the web console is the only place to read them. **Sign in to
OpenCode…** opens a normal browser window that uses Battery's own private profile
(`%AppData%\Battery\WebView2`). Sign in there once and close the window. Battery never sees your
password, and it never touches your main browser.

After that, every 5 minutes Battery loads the console in a hidden copy of that browser and reads the
usage the console page itself fetches (`/console/api/go/status`). Go limits are dollar amounts per
window, and Battery shows them as percent of each window's limit, capped at 100%.

Limits:

- If the session expires, the bars dim and the hover says to sign in again.
- While no 5-hour window is running, the hover says "not started" for it.
- Needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11.

### Known risks

- Any of the three sources can change without notice.
- A topmost window over the taskbar can be disturbed by Windows updates or third-party taskbar tools.
- Passive Claude and Codex readings can be stale for long stretches.

## What Battery stores

Everything lives in `%AppData%\Battery`:

| File | What |
|---|---|
| `settings.json` | strip position, Start with Windows, hidden apps |
| `readings.json` | last reading per app, so a restart shows bars immediately |
| `claude-status.json` | the last blob Claude Code passed to the status line |
| `statusline-chain.txt` | your previous status-line command, while Claude Code is connected |
| `WebView2\` | the private browser profile holding the claude.ai and OpenCode sign-in sessions |
| `error.log` | only if something unexpected went wrong: the error type and where |

Set the environment variable `BATTERY_DEBUG=1` to also write `web-debug.log` for the claude.ai and
OpenCode readers. It holds HTTP statuses and page paths with ids blanked, never page contents.

## Uninstall

1. Right-click › **Disconnect Claude Code**. This restores your previous status line, from a backup
   taken first.
2. Right-click › untick **Start with Windows**. This removes
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Battery`.
3. Right-click › **Quit**, then delete `%AppData%\Battery` and the `dist` folder.
