# Changelog

All notable changes to CodexQuotaTitle are documented in this file.

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
