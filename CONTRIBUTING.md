# Contributing to CodexQuotaTitle

Contributions are welcome. Please keep changes focused, Windows-compatible, and consistent with the profile-scoped helper architecture.

## Prerequisites

- Windows
- .NET 8 SDK
- Codex Desktop for end-to-end plugin validation

## Local workflow

```powershell
dotnet restore CodexQuotaTitle.sln
dotnet build CodexQuotaTitle.sln --configuration Release --no-restore
dotnet run --project tests\CodexQuotaTitle.Tests\CodexQuotaTitle.Tests.csproj --configuration Release --no-build
.\scripts\publish-dev-plugin.ps1
```

Use the local development marketplace for iterative installs. Do not add personal `CODEX_HOME` paths, runtime settings, diagnostics, tokens, cookies, or API keys to commits.

## Pull requests

- Explain the user-visible behavior and add focused tests for code changes.
- Keep public metadata and package artifacts aligned with the release version.
- Verify a Release build and the full test suite before requesting review.
- Do not modify real Codex window titles during diagnostics unless the change explicitly requires it.
