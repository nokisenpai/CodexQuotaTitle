# Changelog

All notable changes to CodexQuotaTitle are documented in this file.

## Unreleased

## [0.1.11] - 2026-10-10

- Start rollout readers and watchers only when App Server is unavailable, avoiding session-history scans while live quotas work.
- Cache profile fallback snapshots per file and read appended bytes with a bounded rewrite check, instead of repeatedly rereading all session logs.
- Reuse the selected cached snapshot and invalidate cached files after replacement, truncation, or deletion.

## [0.1.10] - 2026-09-28

- Recover automatically when a stale helper PID has been reused by an inaccessible Windows process.
- Keep MCP status and helper bootstrap alive when process-liveness checks return Access Denied.

## [0.1.9] - 2026-08-30

- Fix periodic App Server quota resynchronization being postponed indefinitely by frequent rollout changes.
- Run App Server resynchronization independently from rollout and settings watchers.
- Prevent overlapping App Server refreshes by rearming the periodic schedule only after the current refresh completes.

## [0.1.8] - 2026-08-29

- Prefer Codex App Server as the live quota source, with session rollout files as an automatic fallback.
- Prevent stale rollout snapshots from overriding fresher App Server quotas during notifications and periodic resynchronization.
- Expose App Server connection, executable-resolution, and fallback diagnostics through MCP status.
- Harden Codex Desktop executable resolution with `QueryFullProcessImageNameW`, while retaining compatibility fallbacks without requiring a separate CLI.
- Improve generated `settings.json` documentation for `dateFormat`, reset-date placeholders, and .NET date/time format examples.

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
