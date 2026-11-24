# Security

Battery keeps sign-in sessions for claude.ai and OpenCode in its own browser profile
(`%AppData%\Battery\WebView2`) and can edit Claude Code's `settings.json`. If you find a way it could
leak those sessions, read more than it says it does, or be made to run something it should not,
please tell me privately.

## Reporting a problem

Use **Report a vulnerability** on this repository's
[Security tab](https://github.com/brandonleeblack/Battery-AI/security/advisories/new). Please do not
open a public issue for security problems.

Include what you did, what happened, and the Battery version (right-click the exe › Properties ›
Details). Leave out passwords, cookies, session tokens and account ids.

I will reply as soon as I can. Battery is a one-person spare-time project, so there is no fixed
response time, but security reports come first.

## Supported versions

Only the latest release gets fixes.

## What Battery does and does not do

- It never sees or stores passwords; you sign in on the service's own page inside Battery's window.
- It never reads cookies, your main browser, or the logins stored by Claude Desktop, Claude Code or Codex.
- It sends nothing anywhere. The only network traffic is the hidden browser loading the services' own
  usage pages, as you would yourself.
- `BATTERY_DEBUG=1` logs HTTP statuses and page paths with ids blanked, never page contents.
