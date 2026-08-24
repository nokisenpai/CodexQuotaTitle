# Changelog

All notable changes to CodexQuotaTitle are documented in this file.

## [0.1.7] - 2026-08-24

- Fall back to the newest profile-scoped rollout quota snapshot when SessionStart does not provide an active session.

## [0.1.6] - 2026-08-23

- Read quota windows from the active Codex Desktop session rollout instead of launching a second Codex App Server.
- Correlate rollouts to SessionStart `session_id` and follow session/task changes without restarting the profile helper.

## [0.1.5] - 2026-08-23

- Resolve the Codex App Server executable from the current Desktop host process before falling back to `PATH`.

## [0.1.4] - 2026-08-23

- Added safe App Server startup diagnostics to the MCP status response.

## [0.1.3] - 2026-08-23

- Added MCP tools to locate and open the current profile's settings file.

## [0.1.2] - 2026-08-23

- Prevented duplicate helper launches when simultaneous MCP instances bootstrap the same profile.
- Stops a helper promptly when its Codex Desktop host exits during startup.

## [0.1.1] - 2026-08-22

- `%base_title%` is now optional, allowing fully custom window titles.

## [0.1.0] - 2026-08-22

- First public release.
- Automatic Codex quota display in the correlated Codex Desktop window title.
- Separate profile-scoped settings and helper instances.
- Live quota refresh through Codex App Server notifications and periodic resynchronization.
- Configurable title templates through bundled MCP tools and `settings.json`.
- Safe, ownership-aware title updates that leave ambiguous window correlations untouched.
