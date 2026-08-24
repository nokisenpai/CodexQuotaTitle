using CodexQuotaTitle.Quotas;
using CodexQuotaTitle.Profiles;
using CodexQuotaTitle.Titles;
using CodexQuotaTitle.Integration;
using CodexQuotaTitle.Mcp;
using CodexQuotaTitle.Accounts;
using CodexQuotaTitle.AppServer;
using CodexQuotaTitle.Settings;
using CodexQuotaTitle.Sessions;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexQuotaTitle.Windows;

var tests = new (string Name, Action Execute)[]
{
    ("Template renders duration-based 5h and week aliases", TemplateRendersDurationAliases),
    ("Template supports content before base title", TemplateSupportsPrefix),
    ("Template accepts zero or one base title placeholder", TemplateAcceptsOptionalBaseTitle),
    ("Template rejects repeated base title placeholders", TemplateRejectsRepeatedBaseTitle),
    ("Template renders a fully custom title", TemplateRendersFullyCustomTitle),
    ("Template preserves unknown placeholders with diagnostics", TemplatePreservesUnknownPlaceholder),
    ("Template supports primary and secondary placeholders", TemplateSupportsPrimaryAndSecondaryPlaceholders),
    ("Template renders missing quota values as em dashes", TemplateRendersMissingValuesAsEmDashes),
    ("Settings create profile defaults atomically", SettingsCreateProfileDefaultsAtomically),
    ("Settings persist template changes per profile", SettingsPersistTemplateChangesPerProfile),
    ("Settings accept line and block comments", SettingsReadJsonWithComments),
    ("Invalid settings JSON keeps the user file intact", SettingsInvalidJsonKeepsUserFile),
    ("MCP get settings returns its exact path and canonical placeholders", () => McpGetSettingsReturnsPathAndSupportedPlaceholders().GetAwaiter().GetResult()),
    ("MCP open settings creates and opens the current profile file", () => McpOpenSettingsCreatesAndUsesCurrentProfilePath().GetAwaiter().GetResult()),
    ("MCP open settings rejects user-provided paths", () => McpOpenSettingsRejectsUserProvidedPath().GetAwaiter().GetResult()),
    ("MCP open settings reports an unavailable profile", () => McpOpenSettingsReportsUnavailableProfile().GetAwaiter().GetResult()),
    ("MCP open settings reports shell failures", () => McpOpenSettingsReportsShellFailure().GetAwaiter().GetResult()),
    ("MCP set template preserves generated JSONC header comments", () => McpSetTemplatePreservesGeneratedHeaderComments().GetAwaiter().GetResult()),
    ("MCP set template preserves arbitrary user comments", () => McpSetTemplatePreservesArbitraryUserComments().GetAwaiter().GetResult()),
    ("MCP set template preserves other settings properties", () => McpSetTemplatePreservesOtherSettingsProperties().GetAwaiter().GetResult()),
    ("MCP set template escapes quotes and Unicode", () => McpSetTemplateEscapesQuotesAndUnicode().GetAwaiter().GetResult()),
    ("MCP set template creates commented settings when absent", () => McpSetTemplateCreatesCommentedSettingsWhenAbsent().GetAwaiter().GetResult()),
    ("MCP set template keeps invalid settings intact", () => McpSetTemplateKeepsInvalidSettingsIntact().GetAwaiter().GetResult()),
    ("Title ownership removes only owned ranges", TitleOwnershipRemovesOnlyOwnedRanges),
    ("Title ownership keeps a replacement title as the base", TitleOwnershipAcceptsReplacementTitle),
    ("Title ownership leaves incomplete markers intact", TitleOwnershipKeepsIncompleteMarker),
    ("Same CODEX_HOME has the same profile identity", ProfileIdentityMatchesSameHome),
    ("Windows-equivalent CODEX_HOME paths have the same identity", ProfileIdentityNormalizesWindowsPathCase),
    ("Different CODEX_HOME values have different identities", ProfileIdentitySeparatesDifferentHomes),
    ("Profile singleton allows one owner per profile", ProfileSingletonAllowsOneOwner),
    ("Profile singleton releases after an asynchronous continuation", ProfileSingletonReleasesAcrossThreads),
    ("Hook input survives launcher-helper serialization", HookInputSurvivesHelperSerialization),
    ("Session state preserves the active session and transcript path", ActiveSessionStatePreservesHookIdentifiers),
    ("Rollout source resolves the direct transcript path", RolloutSourceResolvesDirectTranscriptPath),
    ("Rollout source falls back to session ID without choosing the newest file", RolloutSourceFallsBackToSessionId),
    ("Rollout source falls back when active session metadata is invalid", RolloutSourceRejectsWrongSessionMetadata),
    ("Rollout source finds quotas without an active session", RolloutSourceFallsBackToProfileWithoutActiveSession),
    ("Rollout source selects the newest profile rate-limit snapshot", RolloutSourceSelectsNewestProfileRateLimits),
    ("Rollout source skips newer files without rate limits", RolloutSourceSkipsNewerFilesWithoutRateLimits),
    ("Rollout source keeps the last non-null rate limits", RolloutSourceKeepsLastNonNullRateLimits),
    ("Rollout source supports primary-only and primary-secondary limits", RolloutSourceSupportsQuotaWindowShapes),
    ("Rollout source reads an incrementally completed JSONL line", RolloutSourceReadsIncrementalJsonl),
    ("Rollout source handles append, resync, rotation, and truncation", () => RolloutSourceHandlesFileChanges().GetAwaiter().GetResult()),
    ("Rollout source switches profile fallback when another rollout changes", () => RolloutSourceSwitchesProfileFallbackOnOtherRolloutChange().GetAwaiter().GetResult()),
    ("Rollout source never exposes conversation content", RolloutSourceDoesNotExposeConversationContent),
    ("Rollout source works without any codex executable", RolloutSourceWorksWithoutCodexExecutable),
    ("Bootstrap log records plugin runtime paths", BootstrapLogRecordsPluginRuntimePaths),
    ("Bootstrap log records missing plugin root", BootstrapLogRecordsMissingPluginRoot),
    ("Bootstrap log records missing plugin data", BootstrapLogRecordsMissingPluginData),
    ("Bootstrap log records a missing launcher", BootstrapLogRecordsMissingLauncher),
    ("Bootstrap log records an early helper failure without secrets", BootstrapLogRecordsEarlyHelperFailure),
    ("Bootstrap log tolerates an unwritable target", BootstrapLogToleratesUnwritableTarget),
    ("Helper App Server diagnostics redact sensitive messages", HelperServiceLogRedactsSensitiveAppServerMessages),
    ("MCP startup uses explicit CODEX_HOME", McpStartupContextUsesExplicitCodexHome),
    ("MCP startup resolves the implicit default CODEX_HOME", McpStartupContextResolvesImplicitDefaultCodexHome),
    ("Explicit and implicit CODEX_HOME values share an identity", McpStartupContextMatchesEquivalentExplicitAndImplicitHomes),
    ("MCP does not start a helper when the implicit CODEX_HOME is absent", McpStartupContextDoesNotStartHelperWhenImplicitHomeIsAbsent),
    ("MCP package configuration forwards CODEX_HOME with relative paths", McpPackageConfigurationForwardsCodexHomeRelatively),
    ("MCP starts one helper per profile and forwards host hints", McpBootstrapperStartsOneHelperPerProfile),
    ("Concurrent MCP bootstraps start one helper for one profile", () => McpBootstrapperSerializesConcurrentStarts().GetAwaiter().GetResult()),
    ("MCP replaces a stale dead helper runtime state", McpBootstrapperReplacesStaleState),
    ("MCP can start distinct helpers for distinct profiles", () => McpBootstrapperAllowsDistinctProfiles().GetAwaiter().GetResult()),
    ("App Server executable resolution prioritizes the host process", AppServerExecutableResolutionPrioritizesHostProcess),
    ("App Server executable resolution falls back safely", AppServerExecutableResolutionFallsBackSafely),
    ("Helper uses rollout quotas and exits with the host", () => McpHelperServiceUsesRolloutAndHostLifecycle().GetAwaiter().GetResult()),
    ("Helper uses profile fallback without SessionStart", () => McpHelperServiceUsesProfileFallbackWithoutSessionStart().GetAwaiter().GetResult()),
    ("MCP status retains rollout runtime diagnostics", McpHelperStatusRetainsRolloutDiagnostics),
    ("Helper cleanup restores the base after a fully custom title", () => McpHelperCleanupRestoresBaseAfterFullyCustomTitle().GetAwaiter().GetResult()),
    ("MCP stdio server completes the bootstrap handshake and status call", () => McpStdioServerCompletesBootstrapHandshake().GetAwaiter().GetResult()),
    ("Win32 title round-trip preserves invisible sentinels", Win32TitleRoundTripPreservesSentinels),
    ("Title write test renders the explicit weekly template", TitleWriteTestRendersWeeklyTemplate),
    ("Title write test writes and cleans a controlled Win32 window", () => TitleWriteTestWritesAndCleansControlledWin32Window().GetAwaiter().GetResult()),
    ("Title write test preserves an external base title during cleanup", () => TitleWriteTestPreservesExternalBaseTitle().GetAwaiter().GetResult()),
    ("Title write test stops when ownership is lost", () => TitleWriteTestStopsWhenOwnershipIsLost().GetAwaiter().GetResult()),
    ("Title write test rejects invalid and ambiguous targets", () => TitleWriteTestRejectsInvalidAndAmbiguousTargets().GetAwaiter().GetResult()),
    ("Automatic refresh preserves an external base-title change", AutomaticRefreshPreservesExternalBaseTitle),
    ("Automatic refresh does not duplicate owned content", AutomaticRefreshDoesNotDuplicateOwnedContent),
    ("Disabled settings clean only owned content", AutomaticRefreshDisabledCleansOwnedContent),
    ("Lost ownership prevents destructive cleanup", AutomaticRefreshOwnershipLostPreventsWrite),
    ("Automatic refresh transitions between base and custom templates", AutomaticRefreshTransitionsBetweenBaseAndCustomTemplates),
    ("Fully custom titles do not duplicate ownership on refresh", FullyCustomTitleDoesNotDuplicateOwnership),
    ("Disabled settings clean a fully custom title", FullyCustomTitleDisabledCleansToCapturedBaseTitle),
    ("Fully custom title cleanup preserves lost ownership", FullyCustomTitleOwnershipLossPreventsCleanup),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Execute();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
    }
}

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

return failures.Count == 0 ? 0 : 1;

static void TemplateRendersDurationAliases()
{
    var fiveHourReset = new DateTimeOffset(2026, 8, 18, 14, 20, 0, TimeSpan.Zero);
    var weekReset = new DateTimeOffset(2026, 8, 22, 9, 30, 0, TimeSpan.Zero);
    var quotas = CreateQuotas(
        new RateLimitWindow(37m, 300, fiveHourReset),
        new RateLimitWindow(60m, 10080, weekReset));
    var renderer = new QuotaTemplateRenderer();

    var parts = renderer.RenderParts(
        "%base_title% | %5h_percent_remaining% • %5h_reset_date% | %week_percent_remaining% • %week_reset_date%",
        quotas);

    var expected = string.Concat(
        "Codex - Example Project | 63% • ", fiveHourReset.ToLocalTime().ToString("dd'/'MM HH:mm"),
        " | 40% • ", weekReset.ToLocalTime().ToString("dd'/'MM HH:mm"));
    AssertEqual(expected, parts.Render("Codex - Example Project"));
}

static void TemplateSupportsPrefix()
{
    var quotas = CreateQuotas(null, new RateLimitWindow(25m, 10080, null));
    var parts = new QuotaTemplateRenderer().RenderParts("%week_percent_remaining% • %base_title%", quotas);

    AssertEqual("75% • ", parts.BeforeBaseTitle);
    AssertEqual(string.Empty, parts.AfterBaseTitle);
    AssertEqual("75% • Codex", parts.Render("Codex"));
}

static void TemplateAcceptsOptionalBaseTitle()
{
    var renderer = new QuotaTemplateRenderer();

    AssertTrue(renderer.Validate("%week_percent_remaining%").IsValid, "A template without a base title was rejected.");
    AssertTrue(renderer.Validate("%base_title% | %week_percent_remaining%").IsValid, "A template with one base title was rejected.");
}

static void TemplateRejectsRepeatedBaseTitle() =>
    AssertThrows<FormatException>(() => new QuotaTemplateRenderer().RenderParts("%base_title% | %base_title%", CreateQuotas(null, null)));

static void TemplateRendersFullyCustomTitle()
{
    var resetAt = new DateTimeOffset(2026, 8, 29, 18, 26, 0, TimeSpan.FromHours(2));
    var parts = new QuotaTemplateRenderer().RenderParts(
        "Codex1 • %week_percent_remaining% • %week_reset_date%",
        CreateQuotas(null, new RateLimitWindow(22m, 10080, resetAt)));

    AssertTrue(!parts.IncludesBaseTitle, "A fully custom template was marked as using the base title.");
    AssertEqual(
        string.Concat("Codex1 • 78% • ", resetAt.ToLocalTime().ToString("dd'/'MM HH:mm")),
        parts.Render("Codex - Example Project"));
}

static void TemplatePreservesUnknownPlaceholder()
{
    var renderer = new QuotaTemplateRenderer();
    var validation = renderer.Validate("%base_title% %future_quota%");

    AssertTrue(validation.IsValid, "An unknown placeholder made the template invalid.");
    AssertEqual("%future_quota%", validation.UnknownPlaceholders.Single());
    AssertEqual("Codex %future_quota%", renderer.RenderParts("%base_title% %future_quota%", CreateQuotas(null, null)).Render("Codex"));
}

static void TemplateSupportsPrimaryAndSecondaryPlaceholders()
{
    var primaryReset = new DateTimeOffset(2026, 8, 20, 10, 15, 0, TimeSpan.Zero);
    var secondaryReset = new DateTimeOffset(2026, 8, 21, 11, 30, 0, TimeSpan.Zero);
    var quotas = CreateQuotas(new RateLimitWindow(37m, 300, primaryReset), new RateLimitWindow(60m, 10080, secondaryReset));

    var rendered = new QuotaTemplateRenderer()
        .RenderParts("%base_title% P %primary_percent_used%/%primary_percent_remaining% S %secondary_percent_used%/%secondary_percent_remaining%", quotas)
        .Render("Codex");

    AssertEqual("Codex P 37%/63% S 60%/40%", rendered);
}

static void TemplateRendersMissingValuesAsEmDashes()
{
    var quotas = CreateQuotas(null, new RateLimitWindow(27m, 10080, null));
    var rendered = new QuotaTemplateRenderer()
        .RenderParts("%base_title% 5H %5h_percent_remaining% %5h_reset_date% | W %week_percent_remaining% %week_reset_date%", quotas)
        .Render("Codex");

    AssertEqual("Codex 5H — — | W 73% —", rendered);
}

static void SettingsCreateProfileDefaultsAtomically()
{
    using var fixture = new SettingsFixture();
    var result = fixture.Store.LoadOrCreate();

    AssertTrue(result.UsedDefaults, "Defaults were not used for a new profile.");
    AssertEqual(CodexQuotaTitleSettings.DefaultTemplate, result.Settings.Template);
    AssertTrue(File.Exists(fixture.Store.Paths.SettingsPath), "The default settings file was not created.");
    AssertTrue(!Directory.EnumerateFiles(fixture.Store.Paths.DirectoryPath, "*.tmp").Any(), "Atomic settings creation left a temporary file.");
}

static void SettingsPersistTemplateChangesPerProfile()
{
    using var fixture = new SettingsFixture();
    fixture.Store.Save(CodexQuotaTitleSettings.Defaults with { Template = "%week_percent_remaining% | %base_title%", Enabled = false });
    var loaded = fixture.Store.LoadOrCreate().Settings;

    AssertEqual("%week_percent_remaining% | %base_title%", loaded.Template);
    AssertTrue(!loaded.Enabled, "The persisted enabled setting changed unexpectedly.");
}

static void SettingsInvalidJsonKeepsUserFile()
{
    using var fixture = new SettingsFixture();
    Directory.CreateDirectory(fixture.Store.Paths.DirectoryPath);
    const string invalidJson = "{ invalid settings";
    File.WriteAllText(fixture.Store.Paths.SettingsPath, invalidJson);

    var result = fixture.Store.LoadOrCreate();

    AssertTrue(result.UsedDefaults, "Invalid JSON did not fall back to defaults.");
    AssertEqual("settings-json-invalid", result.Error!);
    AssertEqual(invalidJson, File.ReadAllText(fixture.Store.Paths.SettingsPath));
}

static void SettingsReadJsonWithComments()
{
    using var fixture = new SettingsFixture();
    Directory.CreateDirectory(fixture.Store.Paths.DirectoryPath);
    const string settingsJson = """
        // A fully custom Codex title is allowed.
        {
          /* The weekly quota is displayed first. */
          "template": "Codex1 • %week_percent_remaining% • %week_reset_date%",
          "dateFormat": "dd/MM HH:mm",
          "enabled": true
        }
        """;
    File.WriteAllText(fixture.Store.Paths.SettingsPath, settingsJson);

    var result = fixture.Store.LoadOrCreate();

    AssertTrue(!result.UsedDefaults, "Commented settings unexpectedly fell back to defaults.");
    AssertTrue(result.Error is null, "Commented settings unexpectedly reported an error.");
    AssertEqual("Codex1 • %week_percent_remaining% • %week_reset_date%", result.Settings.Template);
    AssertEqual("dd/MM HH:mm", result.Settings.DateFormat);
    AssertTrue(result.Settings.Enabled, "The commented enabled setting was not loaded.");
}

static async Task McpGetSettingsReturnsPathAndSupportedPlaceholders()
{
    using var fixture = new SettingsFixture();

    using var response = await CallMcpToolAsync(fixture, "codex_quota_title_get_settings");
    var settings = response.RootElement.GetProperty("result").GetProperty("structuredContent");
    var supportedPlaceholders = settings.GetProperty("supportedPlaceholders").EnumerateArray().Select(item => item.GetString()).ToArray();

    AssertEqual(fixture.Store.Paths.SettingsPath, settings.GetProperty("settingsPath").GetString()!);
    AssertTrue(
        supportedPlaceholders.SequenceEqual(QuotaTemplateRenderer.SupportedPlaceholders, StringComparer.Ordinal),
        "The MCP settings response did not use the canonical supported placeholder list.");
}

static async Task McpOpenSettingsCreatesAndUsesCurrentProfilePath()
{
    using var fixture = new SettingsFixture();
    var launcher = new TestSettingsFileLauncher();
    AssertTrue(!File.Exists(fixture.Store.Paths.SettingsPath), "The test settings file already exists.");

    using var response = await CallMcpToolAsync(fixture, "codex_quota_title_open_settings", launcher: launcher);
    var result = response.RootElement.GetProperty("result").GetProperty("structuredContent");

    AssertTrue(result.GetProperty("opened").GetBoolean(), "The MCP open-settings response did not report success.");
    AssertEqual(fixture.Store.Paths.SettingsPath, result.GetProperty("settingsPath").GetString()!);
    AssertTrue(File.Exists(fixture.Store.Paths.SettingsPath), "Open settings did not create the default settings file.");
    AssertEqual(fixture.Store.Paths.SettingsPath, launcher.OpenedPaths.Single());
}

static async Task McpOpenSettingsRejectsUserProvidedPath()
{
    using var fixture = new SettingsFixture();
    var launcher = new TestSettingsFileLauncher();

    using var response = await CallMcpToolAsync(
        fixture,
        "codex_quota_title_open_settings",
        new { path = Path.Combine(Path.GetTempPath(), "not-the-settings-file.json") },
        launcher);
    var error = response.RootElement.GetProperty("error");

    AssertTrue(error.GetProperty("code").GetInt32() == -32602, "Open settings accepted a user-provided path.");
    AssertTrue(launcher.OpenedPaths.Count == 0, "Open settings launched a path supplied by the user.");
}

static async Task McpOpenSettingsReportsUnavailableProfile()
{
    using var fixture = new SettingsFixture();
    var startupContext = new McpStartupContext((string?)null, null);

    using var response = await CallMcpToolAsync(fixture, "codex_quota_title_open_settings", startupContext: startupContext);
    var error = response.RootElement.GetProperty("error");

    AssertTrue(error.GetProperty("code").GetInt32() == -32000, "An unavailable profile did not produce an MCP storage error.");
    AssertContains(error.GetProperty("message").GetString()!, "ProfileIdentity is unavailable");
}

static async Task McpOpenSettingsReportsShellFailure()
{
    using var fixture = new SettingsFixture();
    var launcher = new TestSettingsFileLauncher { Failure = new SettingsFileOpenException("The associated application is unavailable.") };

    using var response = await CallMcpToolAsync(fixture, "codex_quota_title_open_settings", launcher: launcher);
    var error = response.RootElement.GetProperty("error");

    AssertTrue(error.GetProperty("code").GetInt32() == -32000, "A shell failure did not produce an MCP storage error.");
    AssertContains(error.GetProperty("message").GetString()!, "Unable to open settings");
}

static async Task McpSetTemplatePreservesGeneratedHeaderComments()
{
    using var fixture = new SettingsFixture();
    fixture.Store.LoadOrCreate();

    const string template = "Codex1 • %week_percent_remaining%";
    using var response = await CallMcpSetTemplateAsync(fixture, template);
    var contents = File.ReadAllText(fixture.Store.Paths.SettingsPath);

    AssertMcpSetTemplateSucceeded(response, template);
    AssertContains(contents, "// CodexQuotaTitle supported placeholders:");
    AssertContains(contents, "// %base_title%");
    AssertContains(contents, "// %week_percent_remaining%");
}

static async Task McpSetTemplatePreservesArbitraryUserComments()
{
    using var fixture = new SettingsFixture();
    WriteSettings(fixture, """
        // Keep this heading.
        {
          "template": "%base_title%", // Keep this inline note.
          /* Keep this user comment. */
          "dateFormat": "dd/MM HH:mm",
          "enabled": true
        }
        """);

    using var response = await CallMcpSetTemplateAsync(fixture, "W %week_percent_remaining%");
    var contents = File.ReadAllText(fixture.Store.Paths.SettingsPath);

    AssertMcpSetTemplateSucceeded(response, "W %week_percent_remaining%");
    AssertContains(contents, "// Keep this heading.");
    AssertContains(contents, "// Keep this inline note.");
    AssertContains(contents, "/* Keep this user comment. */");
}

static async Task McpSetTemplatePreservesOtherSettingsProperties()
{
    using var fixture = new SettingsFixture();
    WriteSettings(fixture, """
        {
          "template": "%base_title%",
          "dateFormat": "yyyy-MM-dd HH:mm",
          "enabled": false
        }
        """);

    using var response = await CallMcpSetTemplateAsync(fixture, "Codex1 • %week_percent_remaining%");
    var contents = File.ReadAllText(fixture.Store.Paths.SettingsPath);
    var loaded = fixture.Store.LoadOrCreate().Settings;

    AssertMcpSetTemplateSucceeded(response, "Codex1 • %week_percent_remaining%");
    AssertEqual("yyyy-MM-dd HH:mm", loaded.DateFormat);
    AssertTrue(!loaded.Enabled, "The set-template operation changed enabled.");
    AssertTrue(
        contents.IndexOf("\"template\"", StringComparison.Ordinal) < contents.IndexOf("\"dateFormat\"", StringComparison.Ordinal) &&
        contents.IndexOf("\"dateFormat\"", StringComparison.Ordinal) < contents.IndexOf("\"enabled\"", StringComparison.Ordinal),
        "The set-template operation changed the top-level property order.");
}

static async Task McpSetTemplateEscapesQuotesAndUnicode()
{
    using var fixture = new SettingsFixture();
    fixture.Store.LoadOrCreate();
    const string template = "Codex \"Café\" • ☃ %week_percent_remaining%";

    using var response = await CallMcpSetTemplateAsync(fixture, template);
    var contents = File.ReadAllText(fixture.Store.Paths.SettingsPath);
    var loaded = fixture.Store.LoadOrCreate().Settings;

    AssertMcpSetTemplateSucceeded(response, template);
    AssertEqual(template, loaded.Template);
    AssertTrue(contents.Contains("\\", StringComparison.Ordinal), "The updated JSONC string did not escape its embedded quotes.");
}

static async Task McpSetTemplateCreatesCommentedSettingsWhenAbsent()
{
    using var fixture = new SettingsFixture();
    const string template = "Codex1 • %week_percent_remaining%";
    AssertTrue(!File.Exists(fixture.Store.Paths.SettingsPath), "The test settings file already exists.");

    using var response = await CallMcpSetTemplateAsync(fixture, template);
    var contents = File.ReadAllText(fixture.Store.Paths.SettingsPath);
    var loaded = fixture.Store.LoadOrCreate().Settings;

    AssertMcpSetTemplateSucceeded(response, template);
    AssertContains(contents, "// CodexQuotaTitle supported placeholders:");
    AssertEqual(template, loaded.Template);
    AssertEqual(CodexQuotaTitleSettings.DefaultDateFormat, loaded.DateFormat);
    AssertTrue(loaded.Enabled, "Default enabled was not preserved when settings were created.");
}

static async Task McpSetTemplateKeepsInvalidSettingsIntact()
{
    using var fixture = new SettingsFixture();
    const string invalidSettings = "{ invalid settings";
    WriteSettings(fixture, invalidSettings);

    using var response = await CallMcpSetTemplateAsync(fixture, "Codex1 • %week_percent_remaining%");
    var error = response.RootElement.GetProperty("error");

    AssertTrue(error.GetProperty("code").GetInt32() == -32000, "Invalid settings did not produce an MCP storage error.");
    AssertContains(error.GetProperty("message").GetString()!, "settings-json-invalid");
    AssertEqual(invalidSettings, File.ReadAllText(fixture.Store.Paths.SettingsPath));
}

static async Task<JsonDocument> CallMcpSetTemplateAsync(SettingsFixture fixture, string template)
{
    return await CallMcpToolAsync(fixture, "codex_quota_title_set_template", new { template });
}

static async Task<JsonDocument> CallMcpToolAsync(
    SettingsFixture fixture,
    string toolName,
    object? arguments = null,
    ISettingsFileLauncher? launcher = null,
    McpStartupContext? startupContext = null)
{
    startupContext ??= new McpStartupContext(fixture.Profile.RawCodexHome, fixture.Profile);
    var bootstrapper = new McpHelperBootstrapper(
        new McpBootstrapLog(Path.Combine(fixture.Store.Paths.DirectoryPath, "mcp-bootstrap")),
        startupContext,
        new McpProcessTree(null, Array.Empty<McpProcessNode>()));
    var server = new McpStdioServer(
        new McpBootstrapLog(Path.Combine(fixture.Store.Paths.DirectoryPath, "mcp-server")),
        Environment.ProcessId,
        startupContext,
        bootstrapper,
        new TestSettingsStoreFactory(fixture.Store),
        settingsFileLauncher: launcher);
    var request = JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "tools/call",
        @params = new
        {
            name = toolName,
            arguments = arguments ?? new { },
        },
    });
    using var input = new StringReader(request);
    using var output = new StringWriter();

    await server.RunAsync(input, output);
    return JsonDocument.Parse(output.ToString());
}

static void AssertMcpSetTemplateSucceeded(JsonDocument response, string expectedTemplate)
{
    var settings = response.RootElement.GetProperty("result").GetProperty("structuredContent");
    AssertEqual(expectedTemplate, settings.GetProperty("template").GetString()!);
    AssertTrue(settings.GetProperty("helperRefreshRequested").GetBoolean(), "The set-template operation did not request a helper refresh.");
}

static void WriteSettings(SettingsFixture fixture, string contents)
{
    Directory.CreateDirectory(fixture.Store.Paths.DirectoryPath);
    File.WriteAllText(fixture.Store.Paths.SettingsPath, contents);
}

static void TitleOwnershipRemovesOnlyOwnedRanges()
{
    var ownership = new TitleOwnership(new TestSentinels());
    var transformed = ownership.Apply("Codex - Example Project", "quota: 63% | ", " • 18/08 14:20");

    AssertEqual("[start]quota: 63% | [end]Codex - Example Project[start] • 18/08 14:20[end]", transformed);
    AssertEqual("External Codex - Example Project added", ownership.GetBaseTitle("External [start]quota: 63% | [end]Codex - Example Project[start] • 18/08 14:20[end] added"));
}

static void TitleOwnershipAcceptsReplacementTitle()
{
    var ownership = new TitleOwnership(new TestSentinels());

    AssertEqual("Codex - Other project", ownership.GetBaseTitle("Codex - Other project"));
    AssertEqual("[start]quota[end]Codex - Other project", ownership.Apply("Codex - Other project", "quota", string.Empty));
}

static void TitleOwnershipKeepsIncompleteMarker()
{
    var ownership = new TitleOwnership(new TestSentinels());

    AssertEqual("Codex [start]not-complete", ownership.GetBaseTitle("Codex [start]not-complete"));
}

static void ProfileIdentityMatchesSameHome()
{
    var codexHome = Path.Combine(Path.GetTempPath(), "cqt-profile-tests", Guid.NewGuid().ToString("N"));
    var first = ProfileIdentity.Create(codexHome);
    var second = ProfileIdentity.Create(codexHome);

    AssertEqual(first.ProfileHash, second.ProfileHash);
    AssertEqual(first.NormalizedCodexHome, second.NormalizedCodexHome);
}

static void ProfileIdentityNormalizesWindowsPathCase()
{
    var codexHome = Path.Combine(Path.GetTempPath(), "cqt-profile-tests", Guid.NewGuid().ToString("N"));
    var first = ProfileIdentity.Create(codexHome);
    var equivalent = ProfileIdentity.Create(string.Concat(codexHome.ToUpperInvariant(), Path.DirectorySeparatorChar));

    AssertEqual(first.ProfileHash, equivalent.ProfileHash);
}

static void ProfileIdentitySeparatesDifferentHomes()
{
    var root = Path.Combine(Path.GetTempPath(), "cqt-profile-tests", Guid.NewGuid().ToString("N"));
    var first = ProfileIdentity.Create(Path.Combine(root, "first"));
    var second = ProfileIdentity.Create(Path.Combine(root, "second"));

    AssertTrue(!string.Equals(first.ProfileHash, second.ProfileHash, StringComparison.Ordinal), "Different profiles shared a hash.");
}

static void ProfileSingletonAllowsOneOwner()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-profile-tests", Guid.NewGuid().ToString("N")));
    using var first = ProfileSingleton.TryAcquire(profile);
    using var second = ProfileSingleton.TryAcquire(profile);

    AssertTrue(first.IsOwner, "The first profile singleton acquisition did not own the mutex.");
    AssertTrue(!second.IsOwner, "A second helper acquired the profile mutex.");
}

static void ProfileSingletonReleasesAcrossThreads()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-profile-tests", Guid.NewGuid().ToString("N")));
    var first = ProfileSingleton.TryAcquire(profile);
    AssertTrue(first.IsOwner, "The first profile singleton acquisition did not own the synchronization object.");

    Task.Run(first.Dispose).GetAwaiter().GetResult();
    using var second = ProfileSingleton.TryAcquire(profile);
    AssertTrue(second.IsOwner, "The profile synchronization object was not released after a continuation on another thread.");
}

static void HookInputSurvivesHelperSerialization()
{
    var input = new IntegrationHookInput("session-42", "C:\\workspace", "startup", "gpt-5.6-codex", "default", "C:\\sessions\\rollout-session-42.jsonl");
    using var reader = new StringReader(input.SerializeForHelper());
    var restored = IntegrationHookInput.ReadAsync(reader).GetAwaiter().GetResult();

    AssertEqual(input.SessionId!, restored.SessionId!);
    AssertEqual(input.CurrentWorkingDirectory!, restored.CurrentWorkingDirectory!);
    AssertEqual(input.Source!, restored.Source!);
    AssertEqual(input.Model!, restored.Model!);
    AssertEqual(input.PermissionMode!, restored.PermissionMode!);
    AssertEqual(input.TranscriptPath!, restored.TranscriptPath!);
}

static void ActiveSessionStatePreservesHookIdentifiers()
{
    using var fixture = new RolloutFixture();
    var store = new ActiveSessionStore(Path.Combine(fixture.Root, "runtime"));

    AssertTrue(store.Update("session-42", fixture.RolloutPath, DateTimeOffset.UtcNow), "The active session state was not written.");
    var state = store.TryRead() ?? throw new InvalidOperationException("The active session state was not readable.");

    AssertEqual("session-42", state.SessionId);
    AssertEqual(fixture.RolloutPath, state.TranscriptPath!);
}

static void RolloutSourceResolvesDirectTranscriptPath()
{
    using var fixture = new RolloutFixture();
    fixture.WriteRollout("session-direct", [fixture.TokenCount(41m, 10080, 1_788_020_771L, null)]);
    fixture.Store.Update("session-direct", fixture.RolloutPath, DateTimeOffset.UtcNow);

    using var source = new RolloutQuotaSource(fixture.Profile);
    var result = source.Refresh();

    AssertEqual(RolloutQuotaSourceStatuses.WatchingSession, result.Status);
    AssertEqual(RolloutQuotaSelections.ActiveSession, result.QuotaSelection!);
    AssertEqual("session-direct", result.ActiveSessionId!);
    AssertTrue(result.RolloutAvailable, "The direct transcript path was not accepted.");
    AssertEquivalent(41m, result.Quotas!.RateLimits!.Primary!.UsedPercent);
}

static void RolloutSourceFallsBackToSessionId()
{
    using var fixture = new RolloutFixture();
    var olderPath = fixture.RolloutPath;
    fixture.WriteRollout("session-target", [fixture.TokenCount(33m, 10080, 1_788_020_771L, null)], olderPath);
    var newerPath = Path.Combine(fixture.SessionsDirectory, "nested", "rollout-session-other.jsonl");
    fixture.WriteRollout("session-other", [fixture.TokenCount(99m, 300, 1_788_020_771L, null)], newerPath);
    File.SetLastWriteTimeUtc(newerPath, DateTime.UtcNow.AddMinutes(1));
    fixture.Store.Update("session-target", null, DateTimeOffset.UtcNow);

    using var source = new RolloutQuotaSource(fixture.Profile);
    var result = source.Refresh();

    AssertEqual(RolloutQuotaSourceStatuses.WatchingSession, result.Status);
    AssertEqual(RolloutQuotaSelections.ActiveSession, result.QuotaSelection!);
    AssertEquivalent(33m, result.Quotas!.RateLimits!.Primary!.UsedPercent);
}

static void RolloutSourceRejectsWrongSessionMetadata()
{
    using var fixture = new RolloutFixture();
    fixture.WriteRollout("session-other", [fixture.TokenCount(33m, 10080, 1_788_020_771L, null)]);
    fixture.Store.Update("session-target", fixture.RolloutPath, DateTimeOffset.UtcNow);

    using var source = new RolloutQuotaSource(fixture.Profile);
    var result = source.Refresh();

    AssertEqual(RolloutQuotaSourceStatuses.WatchingProfile, result.Status);
    AssertEqual(RolloutQuotaSelections.ProfileFallback, result.QuotaSelection!);
    AssertTrue(result.ActiveSessionId is null, "An invalid active session was retained during profile fallback.");
    AssertEquivalent(33m, result.Quotas!.RateLimits!.Primary!.UsedPercent);
}

static void RolloutSourceFallsBackToProfileWithoutActiveSession()
{
    using var fixture = new RolloutFixture();
    fixture.WriteRollout("session-profile", [fixture.TokenCount(41m, 10080, 1_788_020_771L, null)]);

    using var source = new RolloutQuotaSource(fixture.Profile);
    var result = source.Refresh();

    AssertEqual(RolloutQuotaSourceStatuses.WatchingProfile, result.Status);
    AssertEqual(RolloutQuotaSelections.ProfileFallback, result.QuotaSelection!);
    AssertTrue(result.ActiveSessionId is null, "Profile fallback unexpectedly invented an active session.");
    AssertTrue(result.RolloutAvailable, "Profile fallback did not expose the selected rollout.");
    AssertEquivalent(41m, result.Quotas!.RateLimits!.Primary!.UsedPercent);
}

static void RolloutSourceSelectsNewestProfileRateLimits()
{
    using var fixture = new RolloutFixture();
    var olderSnapshot = fixture.RolloutPath;
    var newerSnapshot = Path.Combine(fixture.SessionsDirectory, "nested", "rollout-newer.jsonl");
    fixture.WriteRollout("session-old", [fixture.TokenCount(19m, 10080, 1_788_020_771L, null, "2026-08-23T01:00:00Z")], olderSnapshot);
    fixture.WriteRollout("session-new", [fixture.TokenCount(73m, 10080, 1_788_020_772L, null, "2026-08-23T02:00:00Z")], newerSnapshot);
    File.SetLastWriteTimeUtc(olderSnapshot, DateTime.UtcNow.AddMinutes(2));
    File.SetLastWriteTimeUtc(newerSnapshot, DateTime.UtcNow.AddMinutes(-2));

    using var source = new RolloutQuotaSource(fixture.Profile);
    var result = source.Refresh();

    AssertEqual(RolloutQuotaSelections.ProfileFallback, result.QuotaSelection!);
    AssertEquivalent(73m, result.Quotas!.RateLimits!.Primary!.UsedPercent);
}

static void RolloutSourceSkipsNewerFilesWithoutRateLimits()
{
    using var fixture = new RolloutFixture();
    var quotaPath = fixture.RolloutPath;
    var noQuotaPath = Path.Combine(fixture.SessionsDirectory, "nested", "rollout-no-quota.jsonl");
    fixture.WriteRollout("session-quota", [fixture.TokenCount(37m, 10080, 1_788_020_771L, null)], quotaPath);
    fixture.WriteRollout("session-no-quota", [fixture.NullRateLimits()], noQuotaPath);
    File.SetLastWriteTimeUtc(noQuotaPath, DateTime.UtcNow.AddMinutes(2));

    using var source = new RolloutQuotaSource(fixture.Profile);
    var result = source.Refresh();

    AssertEqual(RolloutQuotaSourceStatuses.WatchingProfile, result.Status);
    AssertEquivalent(37m, result.Quotas!.RateLimits!.Primary!.UsedPercent);
}

static void RolloutSourceKeepsLastNonNullRateLimits()
{
    using var fixture = new RolloutFixture();
    fixture.WriteRollout("session-last", [
        fixture.TokenCount(19m, 300, 1_788_020_771L, fixture.Window(64m, 10080, 1_788_020_772L)),
        fixture.NullRateLimits(),
        fixture.TokenCount(41m, 10080, 1_788_020_773L, null),
        fixture.NullRateLimits(),
    ]);
    fixture.Store.Update("session-last", fixture.RolloutPath, DateTimeOffset.UtcNow);

    using var source = new RolloutQuotaSource(fixture.Profile);
    var result = source.Refresh();

    AssertEquivalent(41m, result.Quotas!.RateLimits!.Primary!.UsedPercent);
    AssertTrue(result.Quotas.RateLimits.Secondary is null, "A null secondary quota was invented.");
}

static void RolloutSourceSupportsQuotaWindowShapes()
{
    using var fixture = new RolloutFixture();
    fixture.WriteRollout("session-primary", [fixture.TokenCount(25m, 10080, 1_788_020_771L, null)]);
    fixture.Store.Update("session-primary", fixture.RolloutPath, DateTimeOffset.UtcNow);
    using (var primarySource = new RolloutQuotaSource(fixture.Profile))
    {
        var primary = primarySource.Refresh().Quotas!.RateLimits!;
        AssertTrue(primary.Primary is not null && primary.Secondary is null, "Primary-only limits were not preserved.");
    }

    fixture.WriteRollout("session-both", [fixture.TokenCount(25m, 300, 1_788_020_771L, fixture.Window(64m, 10080, 1_788_020_772L))]);
    fixture.Store.Update("session-both", fixture.RolloutPath, DateTimeOffset.UtcNow);
    using var bothSource = new RolloutQuotaSource(fixture.Profile);
    var both = bothSource.Refresh().Quotas!.RateLimits!;
    AssertEquivalent(300, both.Primary!.WindowDurationMins!.Value);
    AssertEquivalent(10080, both.Secondary!.WindowDurationMins!.Value);
}

static void RolloutSourceReadsIncrementalJsonl()
{
    using var fixture = new RolloutFixture();
    var sessionId = "session-incremental";
    File.WriteAllText(fixture.RolloutPath, fixture.SessionMeta(sessionId) + Environment.NewLine + fixture.TokenCount(37m, 10080, 1_788_020_771L, null));
    fixture.Store.Update(sessionId, fixture.RolloutPath, DateTimeOffset.UtcNow);
    using var source = new RolloutQuotaSource(fixture.Profile);

    AssertEqual(RolloutQuotaSourceStatuses.RateLimitsUnavailable, source.Refresh().Status);
    File.AppendAllText(fixture.RolloutPath, Environment.NewLine);
    var result = source.Refresh();
    AssertEquivalent(37m, result.Quotas!.RateLimits!.Primary!.UsedPercent);
}

static async Task RolloutSourceHandlesFileChanges()
{
    using var fixture = new RolloutFixture();
    fixture.WriteRollout("session-watch", [fixture.TokenCount(10m, 10080, 1_788_020_771L, null)]);
    fixture.Store.Update("session-watch", fixture.RolloutPath, DateTimeOffset.UtcNow);
    using var source = new RolloutQuotaSource(fixture.Profile);
    AssertEquivalent(10m, source.Refresh().Quotas!.RateLimits!.Primary!.UsedPercent);

    File.AppendAllText(fixture.RolloutPath, fixture.TokenCount(22m, 10080, 1_788_020_772L, null) + Environment.NewLine);
    await source.WaitForChangeAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
    AssertEquivalent(22m, source.Refresh().Quotas!.RateLimits!.Primary!.UsedPercent);

    File.WriteAllText(fixture.RolloutPath, fixture.SessionMeta("session-watch") + Environment.NewLine + fixture.TokenCount(55m, 10080, 1_788_020_773L, null) + Environment.NewLine);
    AssertEquivalent(55m, source.Refresh().Quotas!.RateLimits!.Primary!.UsedPercent);

    fixture.WriteRollout("session-next", [fixture.TokenCount(66m, 300, 1_788_020_774L, null)]);
    fixture.Store.Update("session-next", fixture.RolloutPath, DateTimeOffset.UtcNow);
    AssertEquivalent(66m, source.Refresh().Quotas!.RateLimits!.Primary!.UsedPercent);
}

static async Task RolloutSourceSwitchesProfileFallbackOnOtherRolloutChange()
{
    using var fixture = new RolloutFixture();
    var firstPath = fixture.RolloutPath;
    var secondPath = Path.Combine(fixture.SessionsDirectory, "nested", "rollout-other.jsonl");
    fixture.WriteRollout("session-first", [fixture.TokenCount(20m, 10080, 1_788_020_771L, null, "2026-08-23T01:00:00Z")], firstPath);
    fixture.WriteRollout("session-other", [fixture.NullRateLimits()], secondPath);

    using var source = new RolloutQuotaSource(fixture.Profile);
    AssertEquivalent(20m, source.Refresh().Quotas!.RateLimits!.Primary!.UsedPercent);

    File.AppendAllText(secondPath, fixture.TokenCount(61m, 10080, 1_788_020_772L, null, "2026-08-23T02:00:00Z") + Environment.NewLine);
    await source.WaitForChangeAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
    var result = source.Refresh();

    AssertEqual(RolloutQuotaSourceStatuses.WatchingProfile, result.Status);
    AssertEqual(RolloutQuotaSelections.ProfileFallback, result.QuotaSelection!);
    AssertEquivalent(61m, result.Quotas!.RateLimits!.Primary!.UsedPercent);
}

static void RolloutSourceDoesNotExposeConversationContent()
{
    using var fixture = new RolloutFixture();
    const string privateConversation = "private-conversation-must-not-escape";
    fixture.WriteRollout("session-private", [
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"response_item\",\"text\":\"" + privateConversation + "\"}}",
        fixture.TokenCount(42m, 10080, 1_788_020_771L, null),
    ]);

    using var source = new RolloutQuotaSource(fixture.Profile);
    var result = source.Refresh();

    AssertEqual(RolloutQuotaSelections.ProfileFallback, result.QuotaSelection!);
    AssertTrue(!JsonSerializer.Serialize(result).Contains(privateConversation, StringComparison.Ordinal), "Conversation content escaped the rollout quota source.");
}

static void RolloutSourceWorksWithoutCodexExecutable()
{
    using var fixture = new RolloutFixture();
    fixture.WriteRollout("session-no-cli", [fixture.TokenCount(41m, 10080, 1_788_020_771L, null)]);
    fixture.Store.Update("session-no-cli", fixture.RolloutPath, DateTimeOffset.UtcNow);
    AssertTrue(!File.Exists(Path.Combine(fixture.Profile.NormalizedCodexHome, "codex.exe")), "The no-CLI fixture unexpectedly contains codex.exe.");

    using var source = new RolloutQuotaSource(fixture.Profile);
    AssertEqual(RolloutQuotaSourceStatuses.Watching, source.Refresh().Status);
}

static void BootstrapLogRecordsPluginRuntimePaths()
{
    using var fixture = new BootstrapLogFixture();
    var log = fixture.CreateLog();

    log.TryAppend("SESSION_START_CMD_ENTERED");
    log.AppendEnvironmentVariable("PLUGIN_ROOT", "C:\\plugins\\codex-quota-title");
    log.AppendEnvironmentVariable("PLUGIN_DATA", "C:\\plugin-data\\codex-quota-title");

    var contents = fixture.ReadContents();
    AssertContains(contents, "SESSION_START_CMD_ENTERED");
    AssertContains(contents, "PLUGIN_ROOT_PRESENT=TRUE PLUGIN_ROOT_VALUE=C:\\plugins\\codex-quota-title");
    AssertContains(contents, "PLUGIN_DATA_PRESENT=TRUE PLUGIN_DATA_VALUE=C:\\plugin-data\\codex-quota-title");
}

static void BootstrapLogRecordsMissingPluginRoot()
{
    using var fixture = new BootstrapLogFixture();
    var log = fixture.CreateLog();

    log.AppendEnvironmentVariable("PLUGIN_ROOT", null);
    log.TryAppend("LAUNCHER_NOT_STARTED_PLUGIN_ROOT_MISSING");

    var contents = fixture.ReadContents();
    AssertContains(contents, "PLUGIN_ROOT_PRESENT=FALSE");
    AssertContains(contents, "LAUNCHER_NOT_STARTED_PLUGIN_ROOT_MISSING");
}

static void BootstrapLogRecordsMissingPluginData()
{
    using var fixture = new BootstrapLogFixture();
    var log = fixture.CreateLog();
    using var pluginRoot = new EnvironmentVariableScope("PLUGIN_ROOT", "C:\\plugins\\codex-quota-title");
    using var pluginData = new EnvironmentVariableScope("PLUGIN_DATA", null);

    AssertThrows<InvalidOperationException>(() =>
        new IntegrationProbeRunner(log)
            .RunAsync(new IntegrationHookInput("session-42", "C:\\workspace", "startup", "gpt-5.6-codex", "default"))
            .GetAwaiter()
            .GetResult());

    var contents = fixture.ReadContents();
    AssertContains(contents, "HELPER_PLUGIN_RUNTIME_PATHS_REQUESTED");
    AssertContains(contents, "HELPER_FAILED_STAGE=plugin-runtime-paths EXCEPTION_TYPE=InvalidOperationException");
}

static void BootstrapLogRecordsMissingLauncher()
{
    using var fixture = new BootstrapLogFixture();
    var log = fixture.CreateLog();

    log.TryAppend("LAUNCHER_NOT_STARTED_EXECUTABLE_MISSING");

    AssertContains(fixture.ReadContents(), "LAUNCHER_NOT_STARTED_EXECUTABLE_MISSING");
}

static void BootstrapLogRecordsEarlyHelperFailure()
{
    using var fixture = new BootstrapLogFixture();
    var log = fixture.CreateLog();

    log.TryAppend("HELPER_ENTERED");
    log.AppendException("HELPER_FAILED", new InvalidOperationException("token=not-for-diagnostics"));

    var contents = fixture.ReadContents();
    AssertContains(contents, "HELPER_ENTERED");
    AssertContains(contents, "HELPER_FAILED EXCEPTION_TYPE=InvalidOperationException MESSAGE=[REDACTED]");
    AssertTrue(!contents.Contains("not-for-diagnostics", StringComparison.Ordinal), "The bootstrap log wrote a token-like value.");
}

static void BootstrapLogToleratesUnwritableTarget()
{
    var path = Path.Combine(Path.GetTempPath(), "cqt-bootstrap-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "not a directory");
    try
    {
        var log = new HookBootstrapLog(path);
        AssertTrue(!log.TryAppend("SESSION_START_CMD_ENTERED"), "The bootstrap log unexpectedly wrote through a file path.");
    }
    finally
    {
        File.Delete(path);
    }
}

static void HelperServiceLogRedactsSensitiveAppServerMessages()
{
    var directoryPath = Path.Combine(Path.GetTempPath(), "cqt-helper-log-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var log = new HelperServiceLog(directoryPath);
        log.AppendException("APP_SERVER_UNAVAILABLE", new CodexAppServerException("token=not-for-diagnostics"));
        var contents = File.ReadAllText(Path.Combine(directoryPath, "helper-service.log"));

        AssertContains(contents, "APP_SERVER_UNAVAILABLE EXCEPTION_TYPE=CodexAppServerException MESSAGE=[REDACTED]");
        AssertTrue(!contents.Contains("not-for-diagnostics", StringComparison.Ordinal), "The helper log wrote a token-like value.");
    }
    finally
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}

static void McpStartupContextUsesExplicitCodexHome()
{
    const string codexHome = "C:\\test\\codex";
    var startupContext = McpStartupContext.FromResolver(new CodexHomeResolver(
        () => codexHome,
        () => "C:\\test",
        path => string.Equals(path, codexHome, StringComparison.Ordinal)));
    var expectedIdentity = ProfileIdentity.Create(codexHome);

    AssertTrue(startupContext.CodexHomeObserved, "The MCP startup context did not observe explicit CODEX_HOME.");
    AssertTrue(startupContext.CodexHomeAvailable, "The explicit CODEX_HOME was not available.");
    AssertEqual("EXPLICIT", startupContext.CodexHomeSource!);
    AssertEqual(codexHome, startupContext.CodexHome!);
    AssertTrue(startupContext.ProfileIdentity is not null, "The MCP startup context did not create a profile identity.");
    AssertEqual(expectedIdentity.ProfileHash, startupContext.ProfileIdentity!.ProfileHash);
}

static void McpStartupContextDoesNotStartHelperWhenImplicitHomeIsAbsent()
{
    using var fixture = new McpProcessFixture();
    var startupContext = McpStartupContext.FromResolver(new CodexHomeResolver(
        static () => null,
        () => fixture.DirectoryPath,
        static _ => false));
    var bootstrapLog = new McpBootstrapLog(Path.Combine(fixture.DirectoryPath, "CodexQuotaTitle"));
    bootstrapLog.WriteProcessEntered(startupContext, McpProcessTree.Capture(Environment.ProcessId));
    var liveness = new TestProcessLiveness();
    var starter = new TestHelperProcessStarter(liveness);
    var bootstrapper = new McpHelperBootstrapper(bootstrapLog, startupContext, CreateDesktopProcessTree(), starter, liveness);
    var result = bootstrapper.EnsureHelper();

    AssertTrue(!startupContext.CodexHomeObserved, "The MCP startup context reported missing CODEX_HOME as present.");
    AssertTrue(!startupContext.CodexHomeAvailable, "The unavailable implicit CODEX_HOME was treated as available.");
    AssertTrue(startupContext.ProfileIdentity is null, "The MCP startup context invented a profile identity without CODEX_HOME.");
    AssertTrue(!result.StartedNewHelper && starter.Requests.Count == 0, "The MCP started a helper for an unavailable implicit CODEX_HOME.");
    var bootstrapContents = File.ReadAllText(fixture.BootstrapLogPath);
    AssertContains(bootstrapContents, "CODEX_HOME_PRESENT=FALSE");
    AssertContains(bootstrapContents, "CODEX_HOME_SOURCE=IMPLICIT_DEFAULT");
    AssertContains(bootstrapContents, "CODEX_HOME_UNAVAILABLE");
    AssertContains(bootstrapContents, "PROFILE_IDENTITY_UNAVAILABLE");
    AssertContains(bootstrapContents, "MCP_HELPER_NOT_STARTED_CODEX_HOME_UNAVAILABLE");
    AssertTrue(!bootstrapContents.Contains("PROFILE_IDENTITY=", StringComparison.Ordinal), "The MCP bootstrap log invented a profile identity for an unavailable CODEX_HOME.");
}

static void McpStartupContextResolvesImplicitDefaultCodexHome()
{
    using var fixture = new McpProcessFixture();
    var codexHome = Path.Combine(fixture.DirectoryPath, ".codex");
    Directory.CreateDirectory(codexHome);
    var startupContext = McpStartupContext.FromResolver(new CodexHomeResolver(
        static () => null,
        () => fixture.DirectoryPath,
        Directory.Exists));

    AssertTrue(!startupContext.CodexHomeObserved, "The implicit CODEX_HOME was incorrectly reported as explicit.");
    AssertTrue(startupContext.CodexHomeAvailable, "The implicit CODEX_HOME was not available.");
    AssertEqual("IMPLICIT_DEFAULT", startupContext.CodexHomeSource!);
    AssertEqual(codexHome, startupContext.CodexHome!);
    AssertTrue(startupContext.ProfileIdentity is not null, "The MCP startup context did not create an implicit profile identity.");
}

static void McpStartupContextMatchesEquivalentExplicitAndImplicitHomes()
{
    using var fixture = new McpProcessFixture();
    var codexHome = Path.Combine(fixture.DirectoryPath, ".codex");
    Directory.CreateDirectory(codexHome);
    var explicitContext = McpStartupContext.FromResolver(new CodexHomeResolver(
        () => codexHome,
        () => fixture.DirectoryPath,
        Directory.Exists));
    var implicitContext = McpStartupContext.FromResolver(new CodexHomeResolver(
        static () => null,
        () => fixture.DirectoryPath,
        Directory.Exists));

    AssertTrue(explicitContext.ProfileIdentity is not null && implicitContext.ProfileIdentity is not null, "Equivalent CODEX_HOME values did not produce identities.");
    AssertEqual(explicitContext.ProfileIdentity!.ProfileHash, implicitContext.ProfileIdentity!.ProfileHash);
    AssertTrue(explicitContext.ProfileIdentity.IsCodexHomeExplicit, "The explicit identity lost its source information.");
    AssertTrue(!implicitContext.ProfileIdentity.IsCodexHomeExplicit, "The implicit identity was marked explicit.");
}

static void McpPackageConfigurationForwardsCodexHomeRelatively()
{
    var repositoryRoot = FindRepositoryRoot();
    var configurationPath = Path.Combine(repositoryRoot, "plugins", "codex-quota-title", ".mcp.json");
    using var configuration = JsonDocument.Parse(File.ReadAllText(configurationPath));
    var server = configuration.RootElement.GetProperty("codex_quota_title");
    var command = server.GetProperty("command").GetString()!;

    AssertEqual("./bin/mcp/win-x64/CodexQuotaTitle.Mcp.exe", command);
    AssertEqual(".", server.GetProperty("cwd").GetString()!);
    AssertTrue(!Path.IsPathRooted(command), "The bundled MCP command is absolute.");
    AssertTrue(!command.Contains("CodexQuotaTitle\\plugins", StringComparison.OrdinalIgnoreCase), "The bundled MCP command contains the development path.");

    var environmentVariables = server.GetProperty("env_vars").EnumerateArray().Select(value => value.GetString()).ToArray();
    AssertTrue(environmentVariables.Length == 1 && string.Equals(environmentVariables[0], "CODEX_HOME", StringComparison.Ordinal), "The MCP configuration does not forward only CODEX_HOME.");
}

static void McpBootstrapperStartsOneHelperPerProfile()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-mcp-bootstrap-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = HelperRuntimePaths.ForProfile(profile.ProfileHash);
    try
    {
        var liveness = new TestProcessLiveness();
        var starter = new TestHelperProcessStarter(liveness);
        var processTree = CreateDesktopProcessTree();
        var first = new McpHelperBootstrapper(
            new McpBootstrapLog(runtimePaths.DirectoryPath),
            new McpStartupContext(profile.RawCodexHome, profile),
            processTree,
            starter,
            liveness);
        var second = new McpHelperBootstrapper(
            new McpBootstrapLog(runtimePaths.DirectoryPath),
            new McpStartupContext(profile.RawCodexHome, profile),
            processTree,
            starter,
            liveness);

        var firstResult = first.EnsureHelper();
        var secondResult = second.EnsureHelper();

        AssertTrue(firstResult.StartedNewHelper, "The first MCP did not start the profile helper.");
        AssertTrue(!secondResult.StartedNewHelper, "The second MCP started another helper for the same profile.");
        AssertTrue(starter.Requests.Count == 1, "More than one helper start was requested for the same profile.");
        var provisionalState = new HelperRuntimeStateStore(runtimePaths).TryRead();
        AssertTrue(provisionalState is not null && provisionalState.IsRunning, "The helper provisional runtime state was not written.");
        AssertTrue(provisionalState!.HelperPid == firstResult.HelperPid, "The provisional runtime state did not contain the helper PID.");
        AssertTrue(starter.Requests[0].HostChatGptPid == 333, "HOST_CHATGPT_PID was not forwarded to the helper.");
        AssertTrue(starter.Requests[0].HostCodexPid == 222, "HOST_CODEX_PID was not forwarded to the helper.");
        AssertEqual("mcp", starter.Requests[0].Origin);
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static async Task McpBootstrapperSerializesConcurrentStarts()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-mcp-bootstrap-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = HelperRuntimePaths.ForProfile(profile.ProfileHash);
    try
    {
        var liveness = new TestProcessLiveness();
        var starter = new BlockingTestHelperProcessStarter(liveness);
        var processTree = CreateDesktopProcessTree();
        var first = new McpHelperBootstrapper(new McpBootstrapLog(runtimePaths.DirectoryPath), new McpStartupContext(profile.RawCodexHome, profile), processTree, starter, liveness);
        var second = new McpHelperBootstrapper(new McpBootstrapLog(runtimePaths.DirectoryPath), new McpStartupContext(profile.RawCodexHome, profile), processTree, starter, liveness);

        var firstTask = Task.Run(first.EnsureHelper);
        await starter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTask = Task.Run(second.EnsureHelper);
        await WaitUntilAsync(() => secondTask.Status != TaskStatus.WaitingToRun, TimeSpan.FromSeconds(5));
        AssertTrue(starter.Requests.Count == 1, "The second concurrent MCP reached helper start before the first bootstrap completed.");
        starter.ReleaseStart();

        var results = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(5));
        AssertTrue(results.Count(result => result.StartedNewHelper) == 1, "Simultaneous MCP instances requested more than one helper start.");
        AssertTrue(starter.Requests.Count == 1, "The profile bootstrap gate did not serialize helper start requests.");
        AssertTrue(new HelperRuntimeStateStore(runtimePaths).TryRead()?.HelperPid == results.Single(result => result.StartedNewHelper).HelperPid, "The concurrent bootstrap did not retain the provisional helper PID.");
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static void McpBootstrapperReplacesStaleState()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-mcp-bootstrap-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = HelperRuntimePaths.ForProfile(profile.ProfileHash);
    try
    {
        const int staleHelperPid = 6_999;
        var stateStore = new HelperRuntimeStateStore(runtimePaths);
        stateStore.Write(new HelperRuntimeState(
            profile.ProfileHash,
            "mcp",
            staleHelperPid,
            333,
            222,
            true,
            false,
            null,
            Array.Empty<HelperQuotaSnapshot>(),
            0,
            "WINDOW_CORRELATION_AMBIGUOUS",
            Array.Empty<HelperWindowCandidate>(),
            DateTimeOffset.UtcNow));
        var liveness = new TestProcessLiveness();
        var starter = new TestHelperProcessStarter(liveness);
        var bootstrapper = new McpHelperBootstrapper(
            new McpBootstrapLog(runtimePaths.DirectoryPath),
            new McpStartupContext(profile.RawCodexHome, profile),
            CreateDesktopProcessTree(),
            starter,
            liveness);

        var result = bootstrapper.EnsureHelper();

        AssertTrue(result.StartedNewHelper, "A stale helper state prevented a replacement helper from starting.");
        AssertTrue(result.HelperPid != staleHelperPid, "The stale helper PID was reused.");
        AssertTrue(stateStore.TryRead()?.HelperPid == result.HelperPid, "The stale state was not replaced with the new provisional helper PID.");
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static async Task McpBootstrapperAllowsDistinctProfiles()
{
    var firstProfile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-mcp-bootstrap-tests", Guid.NewGuid().ToString("N"), "first"));
    var secondProfile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-mcp-bootstrap-tests", Guid.NewGuid().ToString("N"), "second"));
    var firstPaths = HelperRuntimePaths.ForProfile(firstProfile.ProfileHash);
    var secondPaths = HelperRuntimePaths.ForProfile(secondProfile.ProfileHash);
    try
    {
        var liveness = new TestProcessLiveness();
        var starter = new TestHelperProcessStarter(liveness);
        var processTree = CreateDesktopProcessTree();

        var first = new McpHelperBootstrapper(new McpBootstrapLog(firstPaths.DirectoryPath), new McpStartupContext(firstProfile.RawCodexHome, firstProfile), processTree, starter, liveness);
        var second = new McpHelperBootstrapper(new McpBootstrapLog(secondPaths.DirectoryPath), new McpStartupContext(secondProfile.RawCodexHome, secondProfile), processTree, starter, liveness);

        var results = await Task.WhenAll(Task.Run(first.EnsureHelper), Task.Run(second.EnsureHelper));
        AssertTrue(results[0].StartedNewHelper, "The first profile helper was not started.");
        AssertTrue(results[1].StartedNewHelper, "The distinct profile helper was not started.");
        AssertTrue(starter.Requests.Count == 2, "Distinct profiles did not receive distinct helper starts.");
        AssertTrue(!string.Equals(starter.Requests[0].Profile.ProfileHash, starter.Requests[1].Profile.ProfileHash, StringComparison.Ordinal), "Distinct profiles shared a helper identity.");
    }
    finally
    {
        DeleteRuntimeDirectory(firstPaths);
        DeleteRuntimeDirectory(secondPaths);
    }
}

static void AppServerExecutableResolutionPrioritizesHostProcess()
{
    var directoryPath = Path.Combine(Path.GetTempPath(), "cqt-app-server-executable-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directoryPath);
    try
    {
        var hostExecutable = Path.Combine(directoryPath, "host-codex.exe");
        var fallbackExecutable = Path.Combine(directoryPath, "path-codex.exe");
        File.WriteAllText(hostExecutable, string.Empty);
        File.WriteAllText(fallbackExecutable, string.Empty);
        var resolver = new CodexAppServerExecutableResolver(
            new TestHostCodexExecutablePathProvider(_ => hostExecutable),
            _ => fallbackExecutable);

        var resolution = resolver.Resolve(222);

        AssertEqual(hostExecutable, resolution.Executable);
        AssertEqual("host-process", resolution.Source);
        AssertTrue(resolution.Available, "The executable from the host process was not marked available.");
    }
    finally
    {
        Directory.Delete(directoryPath, recursive: true);
    }
}

static void AppServerExecutableResolutionFallsBackSafely()
{
    var directoryPath = Path.Combine(Path.GetTempPath(), "cqt-app-server-executable-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directoryPath);
    try
    {
        var fallbackExecutable = Path.Combine(directoryPath, "path-codex.exe");
        File.WriteAllText(fallbackExecutable, string.Empty);

        var absentHostResolver = new CodexAppServerExecutableResolver(
            new TestHostCodexExecutablePathProvider(_ => null),
            _ => fallbackExecutable);
        var absent = absentHostResolver.Resolve(null);
        var dead = absentHostResolver.Resolve(222);
        AssertEqual(fallbackExecutable, absent.Executable);
        AssertEqual("path", absent.Source);
        AssertTrue(absent.Available, "The PATH fallback was not marked available.");
        AssertEqual("path", dead.Source);

        var deniedHostResolver = new CodexAppServerExecutableResolver(
            new TestHostCodexExecutablePathProvider(_ => throw new UnauthorizedAccessException()),
            _ => fallbackExecutable);
        var denied = deniedHostResolver.Resolve(222);
        AssertEqual(fallbackExecutable, denied.Executable);
        AssertEqual("path", denied.Source);

        var unresolved = new CodexAppServerExecutableResolver(
            new TestHostCodexExecutablePathProvider(_ => null),
            _ => "codex").Resolve(222);
        AssertEqual("codex", unresolved.Executable);
        AssertEqual("fallback", unresolved.Source);
        AssertTrue(!unresolved.Available, "An unresolved fallback was incorrectly reported as available.");
    }
    finally
    {
        Directory.Delete(directoryPath, recursive: true);
    }
}

static async Task McpHelperServiceUsesRolloutAndHostLifecycle()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-rollout-helper-tests", Guid.NewGuid().ToString("N"), ".codex"));
    var runtimePaths = new HelperRuntimePaths(Path.Combine(Path.GetTempPath(), "cqt-rollout-helper-tests", Guid.NewGuid().ToString("N")));
    try
    {
        var hostLifetime = new TestHostProcessLifetime();
        var windowProvider = new TestHostWindowProvider();
        var titleUpdater = new TestAutomaticTitleUpdateService();
        var settingsFactory = new CodexQuotaTitleSettingsStoreFactory(new LocalAppDataSettingsPathResolver(Path.Combine(runtimePaths.DirectoryPath, "settings")));
        var source = new TestRolloutQuotaSource(new RolloutQuotaReadResult(
            RolloutQuotaSourceStatuses.Watching,
            "session-helper",
            true,
            new QuotaReadResult(new QuotaSnapshot("codex", "Codex", new RateLimitWindow(41m, 10080, DateTimeOffset.UtcNow.AddDays(4)), null), new Dictionary<string, QuotaSnapshot>()),
            DateTimeOffset.UtcNow));
        var service = new McpHelperService(
            hostLifetime,
            windowProvider,
            settingsFactory,
            new TestSettingsChangeWatcherFactory(),
            titleUpdater,
            rolloutQuotaSourceFactory: new TestRolloutQuotaSourceFactory(source));

        var run = service.RunAsync(new McpHelperStartupRequest(profile.RawCodexHome!, profile, 333, 222, "mcp", runtimePaths));
        await windowProvider.Enumerated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stateStore = new HelperRuntimeStateStore(runtimePaths);
        await WaitUntilAsync(() => stateStore.TryRead()?.QuotaSourceStatus == RolloutQuotaSourceStatuses.Watching, TimeSpan.FromSeconds(5));
        var runningState = stateStore.TryRead() ?? throw new InvalidOperationException("The helper did not persist runtime state.");
        AssertEqual("rollout", runningState.QuotaSource);
        AssertEqual("session-helper", runningState.ActiveSessionId!);
        AssertTrue(runningState.Quotas.Single().Primary?.WindowDurationMins == 10080, "The helper did not retain the rollout weekly window.");
        AssertTrue(titleUpdater.Requests.Count > 0, "The helper did not render from rollout quotas.");

        hostLifetime.Complete();
        var finalResult = await run.WaitAsync(TimeSpan.FromSeconds(5));
        AssertTrue(finalResult.AcquiredProfileSingleton, "The primary helper did not own the profile singleton.");
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static async Task McpHelperServiceUsesProfileFallbackWithoutSessionStart()
{
    using var fixture = new RolloutFixture();
    fixture.WriteRollout("session-profile", [fixture.TokenCount(58m, 10080, 1_788_020_771L, null)]);
    var runtimePaths = new HelperRuntimePaths(Path.Combine(Path.GetTempPath(), "cqt-rollout-profile-helper-tests", Guid.NewGuid().ToString("N")));
    try
    {
        var hostLifetime = new TestHostProcessLifetime();
        var windowProvider = new TestHostWindowProvider();
        var titleUpdater = new TestAutomaticTitleUpdateService();
        var settingsFactory = new CodexQuotaTitleSettingsStoreFactory(new LocalAppDataSettingsPathResolver(Path.Combine(runtimePaths.DirectoryPath, "settings")));
        var service = new McpHelperService(
            hostLifetime,
            windowProvider,
            settingsFactory,
            new TestSettingsChangeWatcherFactory(),
            titleUpdater,
            refreshInterval: TimeSpan.FromMilliseconds(50));

        var run = service.RunAsync(new McpHelperStartupRequest(fixture.Profile.RawCodexHome!, fixture.Profile, 333, 222, "mcp", runtimePaths));
        var stateStore = new HelperRuntimeStateStore(runtimePaths);
        await WaitUntilAsync(
            () => stateStore.TryRead() is { QuotaSourceStatus: RolloutQuotaSourceStatuses.WatchingProfile, QuotaSelection: RolloutQuotaSelections.ProfileFallback },
            TimeSpan.FromSeconds(5));
        var runningState = stateStore.TryRead() ?? throw new InvalidOperationException("The helper did not persist profile fallback state.");
        AssertTrue(runningState.ActiveSessionId is null, "The helper invented an active session during fallback.");
        AssertTrue(runningState.Quotas.Single().Primary?.WindowDurationMins == 10080, "The helper did not render profile fallback quotas.");
        AssertTrue(titleUpdater.Requests.Count > 0, "The helper did not update the title from profile fallback quotas.");

        hostLifetime.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static void McpHelperStatusRetainsRolloutDiagnostics()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-rollout-status-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = HelperRuntimePaths.ForProfile(profile.ProfileHash);
    try
    {
        new HelperRuntimeStateStore(runtimePaths).Write(new HelperRuntimeState(
            profile.ProfileHash,
            "mcp",
            7_001,
            333,
            222,
            true,
            false,
            null,
            Array.Empty<HelperQuotaSnapshot>(),
            0,
            "WINDOW_CORRELATION_AMBIGUOUS",
            Array.Empty<HelperWindowCandidate>(),
            DateTimeOffset.UtcNow)
        {
            QuotaSource = "rollout",
            QuotaSourceStatus = RolloutQuotaSourceStatuses.Watching,
            QuotaSelection = RolloutQuotaSelections.ActiveSession,
            ActiveSessionId = "session-status",
            RolloutAvailable = true,
            LastQuotaUpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        var liveness = new TestProcessLiveness();
        liveness.Add(7_001);
        var bootstrapper = new McpHelperBootstrapper(
            new McpBootstrapLog(runtimePaths.DirectoryPath),
            new McpStartupContext(profile.RawCodexHome, profile),
            new McpProcessTree(null, Array.Empty<McpProcessNode>()),
            processLiveness: liveness);

        var status = bootstrapper.GetStatus();
        AssertEqual("rollout", status.QuotaSource);
        AssertEqual(RolloutQuotaSourceStatuses.Watching, status.QuotaSourceStatus);
        AssertEqual(RolloutQuotaSelections.ActiveSession, status.QuotaSelection!);
        AssertEqual("session-status", status.ActiveSessionId!);
        AssertTrue(status.RolloutAvailable, "The rollout availability was not retained.");
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

#pragma warning disable CS8321 // Retained App Server test scaffolding covers optional CLI/dev code.
static async Task HelperFallsBackWhenHostExecutableDisappears()
{
    var directoryPath = Path.Combine(Path.GetTempPath(), "cqt-app-server-executable-tests", Guid.NewGuid().ToString("N"));
    var profile = ProfileIdentity.Create(Path.Combine(directoryPath, ".codex"));
    var runtimePaths = new HelperRuntimePaths(Path.Combine(directoryPath, "runtime"));
    Directory.CreateDirectory(profile.RawCodexHome!);
    try
    {
        var hostExecutable = Path.Combine(directoryPath, "host-codex.exe");
        var fallbackExecutable = Path.Combine(directoryPath, "path-codex.exe");
        File.WriteAllText(hostExecutable, string.Empty);
        File.WriteAllText(fallbackExecutable, string.Empty);
        var appServerFactory = new HostExecutableDisappearsAppServerSessionFactory(hostExecutable);
        var hostLifetime = new TestHostProcessLifetime();
        var executableResolver = new CodexAppServerExecutableResolver(
            new TestHostCodexExecutablePathProvider(_ => hostExecutable),
            _ => fallbackExecutable);
        var settingsFactory = new CodexQuotaTitleSettingsStoreFactory(new LocalAppDataSettingsPathResolver(Path.Combine(directoryPath, "settings")));
        var service = new McpHelperService(
            appServerFactory,
            hostLifetime,
            new TestHostWindowProvider(),
            settingsFactory,
            new TestSettingsChangeWatcherFactory(),
            new TestAutomaticTitleUpdateService(),
            appServerExecutableResolver: executableResolver);

        var run = service.RunAsync(new McpHelperStartupRequest(profile.RawCodexHome!, profile, 333, 222, "mcp", runtimePaths));
        await appServerFactory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stateStore = new HelperRuntimeStateStore(runtimePaths);
        await WaitUntilAsync(() => stateStore.TryRead()?.AppServerConnected == true, TimeSpan.FromSeconds(5));
        var state = stateStore.TryRead() ?? throw new InvalidOperationException("The helper did not persist runtime state.");
        AssertTrue(appServerFactory.Executables.SequenceEqual([hostExecutable, fallbackExecutable]), "The helper did not retry with the fallback executable.");
        AssertEqual("path", state.AppServerExecutableSource);
        AssertTrue(state.AppServerExecutableAvailable == true, "The fallback executable was not recorded as available.");

        hostLifetime.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally
    {
        Directory.Delete(directoryPath, recursive: true);
    }
}

static async Task McpHelperServiceUsesBootstrapHintsAndHostLifecycle()
{
    var userProfileDirectory = Path.Combine(Path.GetTempPath(), "cqt-mcp-helper-tests", Guid.NewGuid().ToString("N"));
    var codexHome = Path.Combine(userProfileDirectory, ".codex");
    Directory.CreateDirectory(codexHome);
    var hostExecutable = Path.Combine(userProfileDirectory, "codex.exe");
    File.WriteAllText(hostExecutable, string.Empty);
    var startupContext = McpStartupContext.FromResolver(new CodexHomeResolver(
        static () => null,
        () => userProfileDirectory,
        Directory.Exists));
    var profile = startupContext.ProfileIdentity ?? throw new InvalidOperationException("The implicit test profile was unavailable.");
    var runtimePaths = new HelperRuntimePaths(Path.Combine(Path.GetTempPath(), "cqt-mcp-helper-tests", Guid.NewGuid().ToString("N")));
    try
    {
        var appServerFactory = new TestAppServerSessionFactory();
        var hostLifetime = new TestHostProcessLifetime();
        var windowProvider = new TestHostWindowProvider();
        var request = new McpHelperStartupRequest(startupContext.CodexHome!, profile, 333, 222, "mcp", runtimePaths);
        var settingsFactory = new CodexQuotaTitleSettingsStoreFactory(new LocalAppDataSettingsPathResolver(Path.Combine(runtimePaths.DirectoryPath, "settings")));
        var executableResolver = new CodexAppServerExecutableResolver(
            new TestHostCodexExecutablePathProvider(_ => hostExecutable),
            _ => throw new InvalidOperationException("The host executable should take priority."));
        var service = new McpHelperService(
            appServerFactory,
            hostLifetime,
            windowProvider,
            settingsFactory,
            new TestSettingsChangeWatcherFactory(),
            appServerExecutableResolver: executableResolver);

        var firstHelper = service.RunAsync(request);
        await appServerFactory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await windowProvider.Enumerated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stateStore = new HelperRuntimeStateStore(runtimePaths);
        await Task.Delay(100);
        var runningState = stateStore.TryRead();
        AssertTrue(runningState is not null && runningState.IsRunning, "The helper did not persist a running state.");
        AssertEqual(startupContext.CodexHome!, appServerFactory.CodexHomes.Single());
        AssertEqual(hostExecutable, appServerFactory.Executables.Single());
        AssertTrue(windowProvider.RequestedProcessIds.Single() == 333, "Windows were not filtered with HOST_CHATGPT_PID.");
        AssertTrue(runningState!.AppServerConnected, "The helper did not report an App Server connection.");
        AssertEqual("connected", runningState.AppServerStatus);
        AssertTrue(runningState.AppServerLastError is null, "A successful App Server connection retained an error.");
        AssertTrue(runningState.AppServerLastExceptionType is null, "A successful App Server connection retained an exception type.");
        AssertEqual("host-process", runningState.AppServerExecutableSource);
        AssertTrue(runningState.AppServerExecutableAvailable == true, "The host executable was not recorded as available.");
        AssertTrue(runningState.Quotas.Count == 1, "The helper did not persist normalized quotas.");
        AssertTrue(runningState.CandidateWindowCount == 2, "The helper did not persist all top-level host windows.");
        AssertEqual("WINDOW_CORRELATION_UNAMBIGUOUS", runningState.WindowCorrelation);
        AssertTrue(runningState.Account is not null && runningState.Account.PlanType == "pro", "The helper did not persist the non-sensitive account summary.");

        var secondFactory = new TestAppServerSessionFactory();
        var secondHelper = await new McpHelperService(secondFactory, new TestHostProcessLifetime(), windowProvider, settingsFactory, new TestSettingsChangeWatcherFactory()).RunAsync(request);
        AssertTrue(!secondHelper.AcquiredProfileSingleton, "A second helper acquired the same profile singleton.");
        AssertTrue(secondFactory.CodexHomes.Count == 0, "The second helper contacted App Server despite the existing singleton.");

        hostLifetime.Complete();
        var finalResult = await firstHelper.WaitAsync(TimeSpan.FromSeconds(5));
        AssertTrue(finalResult.AcquiredProfileSingleton, "The primary helper did not own the profile singleton.");
        var stoppedState = stateStore.TryRead();
        AssertTrue(stoppedState is not null && !stoppedState.IsRunning, "The helper did not stop after HOST_CHATGPT_PID exited.");
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static async Task McpHelperServiceRecordsAppServerStartupDiagnostics()
{
    var timedOut = await RunHelperWithAppServerFailureAsync(new OperationCanceledException("Ignored by the safe timeout diagnostic."));
    AssertEqual("timed-out", timedOut.AppServerStatus);
    AssertEqual("The Codex App Server startup timed out.", timedOut.AppServerLastError!);
    AssertEqual(nameof(OperationCanceledException), timedOut.AppServerLastExceptionType!);
    AssertTrue(!timedOut.AppServerConnected, "A timed-out App Server was reported as connected.");
    AssertTrue(timedOut.LastError is null, "An App Server error changed the title/window error field.");

    var unavailable = await RunHelperWithAppServerFailureAsync(new CodexAppServerException("token=not-for-diagnostics"));
    AssertEqual("unavailable", unavailable.AppServerStatus);
    AssertEqual("[REDACTED]", unavailable.AppServerLastError!);
    AssertEqual(nameof(CodexAppServerException), unavailable.AppServerLastExceptionType!);

    var failed = await RunHelperWithAppServerFailureAsync(new InvalidOperationException("cookie=not-for-diagnostics"));
    AssertEqual("failed", failed.AppServerStatus);
    AssertEqual("[REDACTED]", failed.AppServerLastError!);
    AssertEqual(nameof(InvalidOperationException), failed.AppServerLastExceptionType!);
}

static void McpHelperStatusRetainsAppServerDiagnostics()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-mcp-status-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = HelperRuntimePaths.ForProfile(profile.ProfileHash);
    try
    {
        new HelperRuntimeStateStore(runtimePaths).Write(new HelperRuntimeState(
            profile.ProfileHash,
            "mcp",
            7_001,
            333,
            222,
            false,
            false,
            null,
            Array.Empty<HelperQuotaSnapshot>(),
            0,
            "WINDOW_CORRELATION_AMBIGUOUS",
            Array.Empty<HelperWindowCandidate>(),
            DateTimeOffset.UtcNow)
        {
            AppServerStatus = "unavailable",
            AppServerLastError = "The App Server executable could not be started.",
            AppServerLastExceptionType = nameof(CodexAppServerException),
            AppServerExecutableSource = "fallback",
            AppServerExecutableAvailable = false,
        });
        var bootstrapper = new McpHelperBootstrapper(
            new McpBootstrapLog(runtimePaths.DirectoryPath),
            new McpStartupContext(profile.RawCodexHome, profile),
            new McpProcessTree(null, Array.Empty<McpProcessNode>()));

        var status = bootstrapper.GetStatus();

        AssertEqual("unavailable", status.AppServerStatus);
        AssertEqual("The App Server executable could not be started.", status.AppServerLastError!);
        AssertEqual(nameof(CodexAppServerException), status.AppServerLastExceptionType!);
        AssertEqual("fallback", status.AppServerExecutableSource);
        AssertTrue(status.AppServerExecutableAvailable == false, "The App Server executable availability was not retained.");
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static async Task<HelperRuntimeState> RunHelperWithAppServerFailureAsync(Exception exception)
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-app-server-diagnostics-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = new HelperRuntimePaths(Path.Combine(Path.GetTempPath(), "cqt-app-server-diagnostics-tests", Guid.NewGuid().ToString("N")));
    try
    {
        var hostLifetime = new TestHostProcessLifetime();
        var settingsFactory = new CodexQuotaTitleSettingsStoreFactory(new LocalAppDataSettingsPathResolver(Path.Combine(runtimePaths.DirectoryPath, "settings")));
        var service = new McpHelperService(
            new FailingTestAppServerSessionFactory(exception),
            hostLifetime,
            new TestHostWindowProvider(),
            settingsFactory,
            new TestSettingsChangeWatcherFactory(),
            new TestAutomaticTitleUpdateService());
        var run = service.RunAsync(new McpHelperStartupRequest(profile.RawCodexHome!, profile, 333, 222, "mcp", runtimePaths));
        var stateStore = new HelperRuntimeStateStore(runtimePaths);
        await WaitUntilAsync(() => stateStore.TryRead()?.AppServerStatus != "not-started", TimeSpan.FromSeconds(5));
        hostLifetime.Complete();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));

        return result.FinalState ?? throw new InvalidOperationException("The failed helper did not return a final runtime state.");
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static async Task McpHelperServiceStopsWhenHostExitsDuringInitialization()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-mcp-helper-startup-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = new HelperRuntimePaths(Path.Combine(Path.GetTempPath(), "cqt-mcp-helper-startup-tests", Guid.NewGuid().ToString("N")));
    try
    {
        var appServerFactory = new BlockingTestAppServerSessionFactory();
        var hostLifetime = new TestHostProcessLifetime();
        var titleUpdater = new TestAutomaticTitleUpdateService();
        var settingsFactory = new CodexQuotaTitleSettingsStoreFactory(new LocalAppDataSettingsPathResolver(Path.Combine(runtimePaths.DirectoryPath, "settings")));
        var request = new McpHelperStartupRequest(profile.RawCodexHome!, profile, 333, 222, "mcp", runtimePaths);
        var service = new McpHelperService(
            appServerFactory,
            hostLifetime,
            new TestHostWindowProvider(),
            settingsFactory,
            new TestSettingsChangeWatcherFactory(),
            titleUpdater);

        var run = service.RunAsync(request);
        await appServerFactory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        hostLifetime.Complete();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));

        AssertTrue(result.AcquiredProfileSingleton, "The helper did not acquire the profile singleton before startup.");
        AssertTrue(result.FinalState is not null && !result.FinalState.IsRunning, "The helper did not persist a stopped final state after the host exited during initialization.");
        AssertTrue(titleUpdater.Requests.Count == 1 && !titleUpdater.Requests[0].Enabled, "The helper refreshed the title before stopping during initialization.");
        using var singleton = ProfileSingleton.TryAcquire(profile);
        AssertTrue(singleton.IsOwner, "The helper did not release its profile singleton after the host exited during initialization.");
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static async Task McpHelperCleanupRestoresBaseAfterFullyCustomTitle()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-custom-cleanup-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = new HelperRuntimePaths(Path.Combine(Path.GetTempPath(), "cqt-custom-cleanup-tests", Guid.NewGuid().ToString("N")));
    try
    {
        var hostLifetime = new TestHostProcessLifetime();
        var ownership = new TitleOwnership(new TestSentinels());
        var titleWindow = new TestTitleWriteWindow(333, "Codex - Example Project");
        var settingsFactory = new CodexQuotaTitleSettingsStoreFactory(new LocalAppDataSettingsPathResolver(Path.Combine(runtimePaths.DirectoryPath, "settings")));
        settingsFactory.Create(profile).Save(CodexQuotaTitleSettings.Defaults with
        {
            Template = "Codex1 • %week_percent_remaining% • %week_reset_date%",
        });
        var quotaSource = new TestRolloutQuotaSource(new RolloutQuotaReadResult(
            RolloutQuotaSourceStatuses.Watching,
            "session-custom-cleanup",
            true,
            new QuotaReadResult(new QuotaSnapshot("codex", "Codex", null, new RateLimitWindow(41m, 10080, DateTimeOffset.UtcNow.AddDays(4))), new Dictionary<string, QuotaSnapshot>()),
            DateTimeOffset.UtcNow));
        var service = new McpHelperService(
            hostLifetime,
            new TestSingleWindowHostProvider(titleWindow),
            settingsFactory,
            new TestSettingsChangeWatcherFactory(),
            new AutomaticTitleUpdateService(titleWindow, ownership),
            rolloutQuotaSourceFactory: new TestRolloutQuotaSourceFactory(quotaSource));

        var run = service.RunAsync(new McpHelperStartupRequest(profile.RawCodexHome!, profile, 333, 222, "mcp", runtimePaths));
        await WaitUntilAsync(() => ownership.IsFullyOwnedTitle(titleWindow.Title), TimeSpan.FromSeconds(2));

        hostLifetime.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        AssertEqual("Codex - Example Project", titleWindow.Title);
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static async Task McpHelperRefreshesAfterQuotaNotification()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-notification-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = new HelperRuntimePaths(Path.Combine(Path.GetTempPath(), "cqt-notification-tests", Guid.NewGuid().ToString("N")));
    try
    {
        var factory = new TestAppServerSessionFactory();
        var hostLifetime = new TestHostProcessLifetime();
        var titleUpdater = new TestAutomaticTitleUpdateService();
        var settingsFactory = new CodexQuotaTitleSettingsStoreFactory(new LocalAppDataSettingsPathResolver(Path.Combine(runtimePaths.DirectoryPath, "settings")));
        var service = new McpHelperService(factory, hostLifetime, new TestHostWindowProvider(), settingsFactory, new TestSettingsChangeWatcherFactory(), titleUpdater, refreshInterval: TimeSpan.FromMinutes(1));
        var run = service.RunAsync(new McpHelperStartupRequest(profile.RawCodexHome!, profile, 333, 222, "mcp", runtimePaths));
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        factory.Session.PublishQuota(new QuotaSnapshot("codex", "Codex", new RateLimitWindow(55m, 300, DateTimeOffset.UtcNow.AddHours(1)), null));
        await WaitUntilAsync(() => titleUpdater.Requests.Count >= 2, TimeSpan.FromSeconds(2));
        var state = new HelperRuntimeStateStore(runtimePaths).TryRead();
        AssertTrue(state is not null && state.Quotas.Single().Primary?.UsedPercent == 55m, "The quota notification did not update normalized helper state.");

        hostLifetime.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}

static async Task McpHelperPerformsPeriodicQuotaFallback()
{
    var profile = ProfileIdentity.Create(Path.Combine(Path.GetTempPath(), "cqt-periodic-tests", Guid.NewGuid().ToString("N")));
    var runtimePaths = new HelperRuntimePaths(Path.Combine(Path.GetTempPath(), "cqt-periodic-tests", Guid.NewGuid().ToString("N")));
    try
    {
        var factory = new TestAppServerSessionFactory();
        var hostLifetime = new TestHostProcessLifetime();
        var settingsFactory = new CodexQuotaTitleSettingsStoreFactory(new LocalAppDataSettingsPathResolver(Path.Combine(runtimePaths.DirectoryPath, "settings")));
        var service = new McpHelperService(factory, hostLifetime, new TestHostWindowProvider(), settingsFactory, new TestSettingsChangeWatcherFactory(), new TestAutomaticTitleUpdateService(), refreshInterval: TimeSpan.FromMilliseconds(20));
        var run = service.RunAsync(new McpHelperStartupRequest(profile.RawCodexHome!, profile, 333, 222, "mcp", runtimePaths));
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => factory.Session.ReadRateLimitsCallCount >= 2, TimeSpan.FromSeconds(2));
        AssertTrue(factory.Session.ReadRateLimitsCallCount >= 2, "The periodic fallback did not read rate limits again.");

        hostLifetime.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally
    {
        DeleteRuntimeDirectory(runtimePaths);
    }
}
#pragma warning restore CS8321

static McpProcessTree CreateDesktopProcessTree() =>
    new(
        222,
        [
            new McpProcessNode(111, "CodexQuotaTitle.Mcp.exe"),
            new McpProcessNode(222, "codex.exe"),
            new McpProcessNode(333, "ChatGPT.exe"),
        ]);

static void DeleteRuntimeDirectory(HelperRuntimePaths runtimePaths)
{
    if (Directory.Exists(runtimePaths.DirectoryPath))
    {
        Directory.Delete(runtimePaths.DirectoryPath, recursive: true);
    }
}

static async Task McpStdioServerCompletesBootstrapHandshake()
{
    using var fixture = new McpProcessFixture();
    var codexHome = Path.Combine(fixture.DirectoryPath, "codex-home");
    var packagePath = Path.Combine(FindRepositoryRoot(), "plugins", "codex-quota-title", "bin", "mcp", "win-x64", "CodexQuotaTitle.Mcp.exe");
    var bootstrapLogPath = Path.Combine(Path.GetTempPath(), "CodexQuotaTitle", "mcp-bootstrap.log");
    AssertTrue(File.Exists(packagePath), "The self-contained MCP executable was not published to the plugin package.");
    Directory.CreateDirectory(codexHome);

    var startInfo = new ProcessStartInfo
    {
        FileName = packagePath,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = FindRepositoryRoot(),
    };
    startInfo.Environment["LOCALAPPDATA"] = fixture.DirectoryPath;
    startInfo.Environment["CODEXQUOTATITLE_SETTINGS_ROOT"] = fixture.DirectoryPath;
    startInfo.Environment["CODEX_HOME"] = codexHome;
    startInfo.Environment.Remove("PLUGIN_ROOT");
    startInfo.Environment.Remove("PLUGIN_DATA");

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The MCP server process did not start.");
    await WriteJsonAsync(process, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""");
    using (var initializeResponse = await ReadResponseAsync(process))
    {
        AssertEqual("2025-03-26", initializeResponse.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString()!);
    }

    await WriteJsonAsync(process, """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
    using (var toolsResponse = await ReadResponseAsync(process))
    {
        var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
        AssertEqual("codex_quota_title_status", tools[0].GetProperty("name").GetString()!);
        AssertEqual("codex_quota_title_get_settings", tools[1].GetProperty("name").GetString()!);
        AssertTrue(tools[1].GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean(), "Get settings is not marked read-only.");
        AssertEqual("codex_quota_title_open_settings", tools[2].GetProperty("name").GetString()!);
        AssertTrue(!tools[2].GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean(), "Open settings is incorrectly marked read-only.");
        AssertEqual("codex_quota_title_set_template", tools[3].GetProperty("name").GetString()!);
    }

    await WriteJsonAsync(process, """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"codex_quota_title_status","arguments":{}}}""");
    using (var statusResponse = await ReadResponseAsync(process))
    {
        var status = statusResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
        AssertEqual("active", status.GetProperty("status").GetString()!);
        AssertTrue(status.GetProperty("codexHomeObserved").GetBoolean(), "The MCP status tool did not observe CODEX_HOME.");
        AssertEqual("EXPLICIT", status.GetProperty("codexHomeSource").GetString()!);
        AssertTrue(status.GetProperty("codexHomeAvailable").GetBoolean(), "The MCP status tool did not report an available CODEX_HOME.");
        AssertEqual("rollout", status.GetProperty("quotaSource").GetString()!);
        AssertEqual(RolloutQuotaSourceStatuses.RolloutNotFound, status.GetProperty("quotaSourceStatus").GetString()!);
        AssertEqual(RolloutQuotaSelections.ProfileFallback, status.GetProperty("quotaSelection").GetString()!);
        AssertTrue(status.GetProperty("rolloutAvailable").ValueKind == JsonValueKind.False, "The inactive MCP status unexpectedly reported an active rollout.");
        AssertTrue(status.GetProperty("activeSessionId").ValueKind == JsonValueKind.Null, "The inactive MCP status unexpectedly exposed a session.");
    }

    await WriteJsonAsync(process, """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"codex_quota_title_get_settings","arguments":{}}}""");
    using (var settingsResponse = await ReadResponseAsync(process))
    {
        var settings = settingsResponse.RootElement.GetProperty("result").GetProperty("structuredContent");
        AssertEqual(CodexQuotaTitleSettings.DefaultTemplate, settings.GetProperty("template").GetString()!);
        AssertTrue(settings.GetProperty("enabled").GetBoolean(), "Default settings are not enabled.");
        var expectedSettingsPath = Path.Combine(fixture.DirectoryPath, ProfileIdentity.Create(codexHome).ProfileHash, "settings.json");
        AssertEqual(expectedSettingsPath, settings.GetProperty("settingsPath").GetString()!);
        AssertTrue(
            settings.GetProperty("supportedPlaceholders").EnumerateArray().Select(item => item.GetString()).SequenceEqual(QuotaTemplateRenderer.SupportedPlaceholders, StringComparer.Ordinal),
            "The packaged MCP did not return the canonical placeholder list.");
    }

    await process.StandardInput.DisposeAsync();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    var standardError = await process.StandardError.ReadToEndAsync();
    AssertTrue(process.ExitCode == 0, $"The MCP server exited with {process.ExitCode}: {standardError}");
    AssertEqual(string.Empty, standardError);

    var bootstrapContents = string.Join(
        Environment.NewLine,
        File.ReadLines(bootstrapLogPath).Where(line => line.Contains($"PID={process.Id} ", StringComparison.Ordinal)));
    AssertContains(bootstrapContents, "MCP_PROCESS_ENTERED");
    AssertContains(bootstrapContents, "MCP_PROCESS_CHAIN=");
    AssertContains(bootstrapContents, $"CODEX_HOME_PRESENT=TRUE CODEX_HOME_VALUE={codexHome}");
    AssertContains(bootstrapContents, "CODEX_HOME_SOURCE=EXPLICIT");
    AssertContains(bootstrapContents, $"CODEX_HOME_RESOLVED_VALUE={codexHome}");
    AssertContains(bootstrapContents, $"PROFILE_IDENTITY={ProfileIdentity.Create(codexHome).ProfileHash}");
    AssertContains(bootstrapContents, "PLUGIN_ROOT_PRESENT=FALSE");
    AssertContains(bootstrapContents, "PLUGIN_DATA_PRESENT=FALSE");
    AssertContains(bootstrapContents, "MCP_TOOLS_LIST");
    AssertContains(bootstrapContents, "MCP_STATUS_TOOL_CALLED");
    AssertContains(bootstrapContents, "MCP_GET_SETTINGS_TOOL_CALLED");
}

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "CodexQuotaTitle.sln")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
}

static async Task WriteJsonAsync(Process process, string message)
{
    await process.StandardInput.WriteLineAsync(message);
    await process.StandardInput.FlushAsync();
}

static async Task<JsonDocument> ReadResponseAsync(Process process)
{
    var response = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
    if (response is null)
    {
        var standardError = await process.StandardError.ReadToEndAsync();
        throw new InvalidOperationException($"The MCP server closed stdout before responding: {standardError}");
    }

    return JsonDocument.Parse(response);
}

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (!condition())
    {
        if (DateTimeOffset.UtcNow >= deadline)
        {
            throw new TimeoutException("The expected asynchronous condition was not reached.");
        }

        await Task.Delay(10);
    }
}

static void Win32TitleRoundTripPreservesSentinels()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var result = TitleOwnershipWin32RoundTrip.Run();
    AssertTrue(result.SentinelsSurvived, "The Win32 title round-trip changed the title markers.");
    AssertEqual(result.BaseTitle, result.RecoveredBaseTitle);
}

static void TitleWriteTestRendersWeeklyTemplate()
{
    var resetAt = new DateTimeOffset(2026, 8, 29, 18, 26, 0, TimeSpan.FromHours(2));
    var quotas = CreateQuotas(null, new RateLimitWindow(0m, 10080, resetAt));

    var rendered = new QuotaTemplateRenderer()
        .RenderParts(TitleWriteTestService.DefaultTemplate, quotas)
        .Render("Codex - Example Project");

    AssertEqual("Codex - Example Project | W 100% • 29/08 18:26", rendered);
}

static async Task TitleWriteTestWritesAndCleansControlledWin32Window()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var result = await TitleWriteTestWin32Controlled.RunAsync();

    AssertTrue(result.Applied, $"The controlled Win32 title write was not applied: {result.FailureReason}.");
    AssertTrue(result.RoundTripVerified, "The controlled Win32 title write did not preserve sentinels.");
    AssertTrue(result.CleanupCompleted, "The controlled Win32 title write was not cleaned up.");
    AssertTrue(!result.OwnershipLost, "The controlled Win32 title write lost ownership unexpectedly.");
}

static async Task TitleWriteTestPreservesExternalBaseTitle()
{
    var titleWindow = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var catalog = new TestTitleWriteWindowCatalog(titleWindow);
    var log = new TestTitleWriteLog();
    var delay = new CallbackTitleWriteTestDelay(() =>
    {
        var ownedSuffix = titleWindow.Title["Codex - Example Project".Length..];
        titleWindow.Title = string.Concat("Codex - Renamed Elsewhere", ownedSuffix);
    });
    var service = CreateTitleWriteTestService(catalog, titleWindow, delay, log);

    var result = await service.ExecuteAsync(CreateTitleWriteTestRequest());

    AssertTrue(result.CleanupCompleted, "Cleanup did not complete after the external base-title change.");
    AssertEqual("Codex - Renamed Elsewhere", titleWindow.Title);
    AssertContains(string.Join(Environment.NewLine, log.Markers), "TITLE_TEST_CLEANUP_OK");
}

static async Task TitleWriteTestStopsWhenOwnershipIsLost()
{
    var titleWindow = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var log = new TestTitleWriteLog();
    var service = CreateTitleWriteTestService(
        new TestTitleWriteWindowCatalog(titleWindow),
        titleWindow,
        new CallbackTitleWriteTestDelay(() => titleWindow.Title = "Codex - Replaced Elsewhere"),
        log);

    var result = await service.ExecuteAsync(CreateTitleWriteTestRequest());

    AssertTrue(result.OwnershipLost, "The title write test did not detect lost ownership.");
    AssertTrue(!result.CleanupCompleted, "The title write test wrote after ownership was lost.");
    AssertEqual("Codex - Replaced Elsewhere", titleWindow.Title);
    AssertTrue(titleWindow.SetCallCount == 1, "The title write test attempted a cleanup write after ownership was lost.");
    AssertContains(string.Join(Environment.NewLine, log.Markers), "TITLE_TEST_OWNERSHIP_LOST");
}

static async Task TitleWriteTestRejectsInvalidAndAmbiguousTargets()
{
    var invalidWindow = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project") { IsWindowResult = false };
    var invalidResult = await CreateTitleWriteTestService(
        new TestTitleWriteWindowCatalog(invalidWindow),
        invalidWindow,
        new CallbackTitleWriteTestDelay(static () => { }),
        new TestTitleWriteLog()).ExecuteAsync(CreateTitleWriteTestRequest());
    AssertTrue(!invalidResult.Applied, "The title write test wrote to an invalid HWND.");
    AssertTrue(invalidWindow.SetCallCount == 0, "The title write test called SetWindowText for an invalid HWND.");

    var ambiguousWindow = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var ambiguousCatalog = new TestTitleWriteWindowCatalog(ambiguousWindow) { ReturnAmbiguousCandidates = true };
    var ambiguousResult = await CreateTitleWriteTestService(
        ambiguousCatalog,
        ambiguousWindow,
        new CallbackTitleWriteTestDelay(static () => { }),
        new TestTitleWriteLog()).ExecuteAsync(CreateTitleWriteTestRequest());
    AssertTrue(!ambiguousResult.Applied, "The title write test wrote despite ambiguous correlation.");
    AssertTrue(ambiguousWindow.SetCallCount == 0, "The title write test called SetWindowText for an ambiguous target.");
}

static void AutomaticRefreshPreservesExternalBaseTitle()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var ownership = new TitleOwnership(new TestSentinels());
    var window = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var service = new AutomaticTitleUpdateService(window, ownership);
    var first = service.Apply(CreateAutomaticTitleRequest(window, true));
    AssertTrue(first.Success, "The first automatic title refresh failed.");

    var suffix = window.Title["Codex - Example Project".Length..];
    window.Title = string.Concat("PanelTame - Codex", suffix);
    var second = service.Apply(CreateAutomaticTitleRequest(window, true));

    AssertTrue(second.Success, "The refresh after an external base-title change failed.");
    AssertEqual("PanelTame - Codex", ownership.GetBaseTitle(window.Title));
}

static void AutomaticRefreshDoesNotDuplicateOwnedContent()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var ownership = new TitleOwnership(new TestSentinels());
    var window = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var service = new AutomaticTitleUpdateService(window, ownership);
    AssertTrue(service.Apply(CreateAutomaticTitleRequest(window, true)).Success, "The first automatic title refresh failed.");
    AssertTrue(service.Apply(CreateAutomaticTitleRequest(window, true)).Success, "The second automatic title refresh failed.");

    AssertTrue(CountOccurrences(window.Title, "[start]") == 1, "Two refreshes duplicated the owned title zone.");
    AssertEqual("Codex - Example Project", ownership.GetBaseTitle(window.Title));
}

static void AutomaticRefreshDisabledCleansOwnedContent()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var ownership = new TitleOwnership(new TestSentinels());
    var window = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var service = new AutomaticTitleUpdateService(window, ownership);
    AssertTrue(service.Apply(CreateAutomaticTitleRequest(window, true)).Success, "The automatic title refresh failed.");
    var cleanup = service.Apply(CreateAutomaticTitleRequest(window, false));

    AssertTrue(cleanup.Cleaned, "Disabled settings did not clean the owned title zone.");
    AssertEqual("Codex - Example Project", window.Title);
}

static void AutomaticRefreshOwnershipLostPreventsWrite()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var ownership = new TitleOwnership(new TestSentinels());
    var window = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var service = new AutomaticTitleUpdateService(window, ownership);
    AssertTrue(service.Apply(CreateAutomaticTitleRequest(window, true)).Success, "The automatic title refresh failed.");
    var setCallsBeforeLoss = window.SetCallCount;
    window.Title = "Externally replaced title";
    var cleanup = service.Apply(CreateAutomaticTitleRequest(window, false));

    AssertTrue(!cleanup.Changed, "Cleanup wrote after title ownership was lost.");
    AssertEqual("Externally replaced title", window.Title);
    AssertTrue(window.SetCallCount == setCallsBeforeLoss, "Cleanup modified a title it no longer owned.");
}

static void AutomaticRefreshTransitionsBetweenBaseAndCustomTemplates()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var ownership = new TitleOwnership(new TestSentinels());
    var window = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var service = new AutomaticTitleUpdateService(window, ownership);
    var withBase = service.Apply(CreateAutomaticTitleRequest(window, true));
    AssertTrue(withBase.Success, "The base-title template was not applied.");

    const string customTemplate = "Codex1 • %week_percent_remaining% • %week_reset_date%";
    var withoutBase = service.Apply(CreateAutomaticTitleRequest(window, true, customTemplate, withBase.CustomTitleBaseTitle));
    AssertTrue(withoutBase.Success, "The custom-title template was not applied.");
    AssertEqual("Codex1 • 100% • 29/08 18:26", withoutBase.RenderedTitle!);
    AssertEqual("Codex - Example Project", withoutBase.CustomTitleBaseTitle!);
    AssertTrue(ownership.IsFullyOwnedTitle(window.Title), "The custom title was not represented as one owned range.");

    var restoredBase = service.Apply(CreateAutomaticTitleRequest(window, true, CodexQuotaTitleSettings.DefaultTemplate, withoutBase.CustomTitleBaseTitle));
    AssertTrue(restoredBase.Success, "The base-title template was not restored from a custom title.");
    AssertEqual("Codex - Example Project | W 100% • 29/08 18:26", restoredBase.RenderedTitle!);
    AssertEqual("Codex - Example Project", ownership.GetBaseTitle(window.Title));
}

static void FullyCustomTitleDoesNotDuplicateOwnership()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var ownership = new TitleOwnership(new TestSentinels());
    var window = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var service = new AutomaticTitleUpdateService(window, ownership);
    const string customTemplate = "Codex1 • %week_percent_remaining% • %week_reset_date%";

    var first = service.Apply(CreateAutomaticTitleRequest(window, true, customTemplate));
    var second = service.Apply(CreateAutomaticTitleRequest(window, true, customTemplate, first.CustomTitleBaseTitle));

    AssertTrue(first.Success && second.Success, "A fully custom title refresh failed.");
    AssertTrue(ownership.IsFullyOwnedTitle(window.Title), "The repeated custom refresh created multiple ownership zones.");
    AssertTrue(CountOccurrences(window.Title, "[start]") == 1, "The repeated custom refresh duplicated the ownership marker.");
    AssertEqual("Codex1 • 100% • 29/08 18:26", second.RenderedTitle!);
}

static void FullyCustomTitleDisabledCleansToCapturedBaseTitle()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var ownership = new TitleOwnership(new TestSentinels());
    var window = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var service = new AutomaticTitleUpdateService(window, ownership);
    const string customTemplate = "Codex1 • %week_percent_remaining% • %week_reset_date%";

    var applied = service.Apply(CreateAutomaticTitleRequest(window, true, customTemplate));
    var cleanup = service.Apply(CreateAutomaticTitleRequest(window, false, customTemplate, applied.CustomTitleBaseTitle));

    AssertTrue(applied.Success, "The fully custom title was not applied.");
    AssertTrue(cleanup.Cleaned, "Disabled settings did not clean the fully custom title.");
    AssertEqual("Codex - Example Project", window.Title);
}

static void FullyCustomTitleOwnershipLossPreventsCleanup()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var ownership = new TitleOwnership(new TestSentinels());
    var window = new TestTitleWriteWindow(Environment.ProcessId, "Codex - Example Project");
    var service = new AutomaticTitleUpdateService(window, ownership);
    const string customTemplate = "Codex1 • %week_percent_remaining% • %week_reset_date%";

    var applied = service.Apply(CreateAutomaticTitleRequest(window, true, customTemplate));
    var setCallsBeforeLoss = window.SetCallCount;
    window.Title = "Externally replaced title";
    var cleanup = service.Apply(CreateAutomaticTitleRequest(window, false, customTemplate, applied.CustomTitleBaseTitle));

    AssertTrue(!cleanup.Changed, "Cleanup wrote after fully custom title ownership was lost.");
    AssertEqual("Externally replaced title", window.Title);
    AssertTrue(window.SetCallCount == setCallsBeforeLoss, "Cleanup modified a fully custom title it no longer owned.");
}

static AutomaticTitleUpdateRequest CreateAutomaticTitleRequest(
    TestTitleWriteWindow window,
    bool enabled,
    string? template = null,
    string? customTitleBaseTitle = null) =>
    new(
        Environment.ProcessId,
        AutomaticTitleUpdateService.UnambiguousCorrelation,
        [new WindowCandidate(window.Handle, window.ProcessId, "ChatGPT.exe", AutomaticTitleUpdateService.ExpectedWindowClassName, window.Title, true, Array.Empty<CodexQuotaTitle.Processes.InspectedProcess>(), Array.Empty<int>())],
        enabled,
        template ?? CodexQuotaTitleSettings.DefaultTemplate,
        CodexQuotaTitleSettings.DefaultDateFormat,
        CreateQuotas(null, new RateLimitWindow(0m, 10080, new DateTimeOffset(2026, 8, 29, 18, 26, 0, TimeSpan.FromHours(2)))),
        customTitleBaseTitle);

static int CountOccurrences(string value, string pattern)
{
    var count = 0;
    var offset = 0;
    while ((offset = value.IndexOf(pattern, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += pattern.Length;
    }

    return count;
}

static TitleWriteTestService CreateTitleWriteTestService(
    ITitleWriteTestWindowCatalog catalog,
    ITitleWriteTestWindowAccessor accessor,
    ITitleWriteTestDelay delay,
    ITitleWriteTestLog log) =>
    new(catalog, accessor, delay, log);

static TitleWriteTestRequest CreateTitleWriteTestRequest() =>
    new(
        Environment.ProcessId,
        TitleWriteTestService.UnambiguousCorrelation,
        TitleWriteTestService.DefaultTemplate,
        CreateQuotas(null, new RateLimitWindow(0m, 10080, new DateTimeOffset(2026, 8, 29, 18, 26, 0, TimeSpan.FromHours(2)))));

static QuotaReadResult CreateQuotas(RateLimitWindow? primary, RateLimitWindow? secondary) =>
    new(new QuotaSnapshot("codex", "Codex", primary, secondary), new Dictionary<string, QuotaSnapshot>());

static void AssertEqual(string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{expected}' but got '{actual}'.");
    }
}

static void AssertEquivalent<T>(T expected, T actual)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
    {
        throw new InvalidOperationException($"Expected '{expected}' but got '{actual}'.");
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void AssertContains(string value, string expected)
{
    AssertTrue(value.Contains(expected, StringComparison.Ordinal), $"Expected '{expected}' in '{value}'.");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

file sealed class TestSentinels : ITitleSentinelStrategy
{
    public string StartMarker => "[start]";

    public string EndMarker => "[end]";
}

file sealed class BootstrapLogFixture : IDisposable
{
    private readonly string _directoryPath = Path.Combine(Path.GetTempPath(), "cqt-bootstrap-tests", Guid.NewGuid().ToString("N"));

    public HookBootstrapLog CreateLog() => new(_directoryPath);

    public string ReadContents() => File.ReadAllText(Path.Combine(_directoryPath, "hook-bootstrap.log"));

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}

file sealed class SettingsFixture : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), "cqt-settings-tests", Guid.NewGuid().ToString("N"));

    public SettingsFixture()
    {
        Profile = ProfileIdentity.Create(Path.Combine(_rootDirectory, "profile"));
        Store = new CodexQuotaTitleSettingsStore(new LocalAppDataSettingsPathResolver(_rootDirectory).GetPaths(Profile));
    }

    public ProfileIdentity Profile { get; }

    public CodexQuotaTitleSettingsStore Store { get; }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}

file sealed class TestSettingsStoreFactory : ICodexQuotaTitleSettingsStoreFactory
{
    private readonly CodexQuotaTitleSettingsStore _store;

    public TestSettingsStoreFactory(CodexQuotaTitleSettingsStore store)
    {
        _store = store;
    }

    public CodexQuotaTitleSettingsStore Create(ProfileIdentity profile) => _store;
}

file sealed class TestSettingsFileLauncher : ISettingsFileLauncher
{
    public List<string> OpenedPaths { get; } = [];

    public SettingsFileOpenException? Failure { get; init; }

    public void Open(string settingsPath)
    {
        OpenedPaths.Add(settingsPath);
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}

file sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previousValue;

    public EnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _previousValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _previousValue);
}

file sealed class McpProcessFixture : IDisposable
{
    public McpProcessFixture()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "cqt-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string BootstrapLogPath => Path.Combine(DirectoryPath, "CodexQuotaTitle", "mcp-bootstrap.log");

    public void Dispose()
    {
        for (var attempt = 0; attempt < 50 && Directory.Exists(DirectoryPath); attempt++)
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch (IOException) when (attempt < 49)
            {
                Thread.Sleep(100);
            }
        }
    }
}

file sealed class TestProcessLiveness : IProcessLiveness
{
    private readonly HashSet<int> _runningProcessIds = [];
    private readonly object _sync = new();

    public bool IsRunning(int processId)
    {
        lock (_sync)
        {
            return _runningProcessIds.Contains(processId);
        }
    }

    public void Add(int processId)
    {
        lock (_sync)
        {
            _runningProcessIds.Add(processId);
        }
    }
}

file sealed class TestHelperProcessStarter : IHelperProcessStarter
{
    private readonly TestProcessLiveness _liveness;
    private int _nextProcessId = 7_000;

    public TestHelperProcessStarter(TestProcessLiveness liveness)
    {
        _liveness = liveness;
    }

    private readonly List<McpHelperStartupRequest> _requests = [];
    private readonly object _sync = new();

    public IReadOnlyList<McpHelperStartupRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    public int Start(McpHelperStartupRequest request)
    {
        int helperPid;
        lock (_sync)
        {
            helperPid = _nextProcessId++;
            _requests.Add(request);
        }

        _liveness.Add(helperPid);
        return helperPid;
    }
}

file sealed class BlockingTestHelperProcessStarter : IHelperProcessStarter
{
    private readonly TestProcessLiveness _liveness;
    private readonly List<McpHelperStartupRequest> _requests = [];
    private readonly object _sync = new();
    private int _nextProcessId = 8_000;

    public BlockingTestHelperProcessStarter(TestProcessLiveness liveness)
    {
        _liveness = liveness;
    }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<McpHelperStartupRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    public int Start(McpHelperStartupRequest request)
    {
        int helperPid;
        lock (_sync)
        {
            helperPid = _nextProcessId++;
            _requests.Add(request);
        }

        _liveness.Add(helperPid);
        Started.TrySetResult();
        Release.Task.GetAwaiter().GetResult();
        return helperPid;
    }

    public void ReleaseStart() => Release.TrySetResult();
}

file sealed class TestHostProcessLifetime : IHostProcessLifetime
{
    private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitForExitAsync(int processId, CancellationToken cancellationToken) =>
        _exit.Task.WaitAsync(cancellationToken);

    public void Complete() => _exit.TrySetResult();
}

file sealed class TestRolloutQuotaSourceFactory : IRolloutQuotaSourceFactory
{
    private readonly IRolloutQuotaSource _source;

    public TestRolloutQuotaSourceFactory(IRolloutQuotaSource source)
    {
        _source = source;
    }

    public IRolloutQuotaSource Create(ProfileIdentity profile) => _source;
}

file sealed class TestRolloutQuotaSource : IRolloutQuotaSource
{
    private readonly RolloutQuotaReadResult _result;

    public TestRolloutQuotaSource(RolloutQuotaReadResult result)
    {
        _result = result;
    }

    public RolloutQuotaReadResult Refresh() => _result;

    public Task WaitForChangeAsync(CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

    public void Dispose()
    {
    }
}

file sealed class RolloutFixture : IDisposable
{
    public RolloutFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "cqt-rollout-tests", Guid.NewGuid().ToString("N"));
        var codexHome = Path.Combine(Root, ".codex");
        SessionsDirectory = Path.Combine(codexHome, "sessions", "2026", "08", "23");
        Directory.CreateDirectory(SessionsDirectory);
        Profile = ProfileIdentity.Create(codexHome);
        Store = ActiveSessionStore.ForProfile(Profile);
        RolloutPath = Path.Combine(SessionsDirectory, "rollout-session.jsonl");
    }

    public string Root { get; }

    public ProfileIdentity Profile { get; }

    public ActiveSessionStore Store { get; }

    public string SessionsDirectory { get; }

    public string RolloutPath { get; }

    public string SessionMeta(string sessionId) =>
        "{\"type\":\"session_meta\",\"payload\":{\"id\":\"" + sessionId + "\"}}";

    public string Window(decimal usedPercent, int windowMinutes, long resetsAt) =>
        "{\"used_percent\":" + usedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"window_minutes\":" + windowMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"resets_at\":" + resetsAt.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

    public string TokenCount(decimal usedPercent, int windowMinutes, long resetsAt, string? secondary, string timestamp = "2026-08-23T01:41:44Z") =>
        "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"limit_id\":\"codex\",\"limit_name\":\"Codex\",\"plan_type\":\"plus\",\"primary\":" +
        Window(usedPercent, windowMinutes, resetsAt) + ",\"secondary\":" + (secondary ?? "null") + "}}}";

    public string NullRateLimits() =>
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":null}}";

    public void WriteRollout(string sessionId, IEnumerable<string> events, string? path = null)
    {
        var targetPath = path ?? RolloutPath;
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, string.Join(Environment.NewLine, new[] { SessionMeta(sessionId) }.Concat(events)) + Environment.NewLine, Encoding.UTF8);
    }

    public void Dispose()
    {
        var runtimeDirectory = Store.DirectoryPath;
        if (Directory.Exists(runtimeDirectory))
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }

        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

file sealed class TestAppServerSessionFactory : IHelperAppServerSessionFactory
{
    public List<string> CodexHomes { get; } = [];

    public List<string> Executables { get; } = [];

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TestAppServerSession Session { get; } = new();

    public Task<IHelperAppServerSession> StartAsync(CodexAppServerLaunchRequest request, CancellationToken cancellationToken)
    {
        CodexHomes.Add(request.CodexHome);
        Executables.Add(request.Executable);
        Started.TrySetResult();
        return Task.FromResult<IHelperAppServerSession>(Session);
    }
}

file sealed class BlockingTestAppServerSessionFactory : IHelperAppServerSessionFactory
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IHelperAppServerSession> StartAsync(CodexAppServerLaunchRequest request, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The blocking App Server factory unexpectedly completed.");
    }
}

file sealed class HostExecutableDisappearsAppServerSessionFactory : IHelperAppServerSessionFactory
{
    private readonly string _hostExecutable;

    public HostExecutableDisappearsAppServerSessionFactory(string hostExecutable)
    {
        _hostExecutable = hostExecutable;
    }

    public List<string> Executables { get; } = [];

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TestAppServerSession Session { get; } = new();

    public Task<IHelperAppServerSession> StartAsync(CodexAppServerLaunchRequest request, CancellationToken cancellationToken)
    {
        Executables.Add(request.Executable);
        if (Executables.Count == 1)
        {
            File.Delete(_hostExecutable);
            return Task.FromException<IHelperAppServerSession>(new CodexAppServerException("Unable to start codex app-server."));
        }

        Started.TrySetResult();
        return Task.FromResult<IHelperAppServerSession>(Session);
    }
}

file sealed class FailingTestAppServerSessionFactory : IHelperAppServerSessionFactory
{
    private readonly Exception _exception;

    public FailingTestAppServerSessionFactory(Exception exception)
    {
        _exception = exception;
    }

    public Task<IHelperAppServerSession> StartAsync(CodexAppServerLaunchRequest request, CancellationToken cancellationToken) =>
        Task.FromException<IHelperAppServerSession>(_exception);
}

file sealed class TestHostCodexExecutablePathProvider : IHostCodexExecutablePathProvider
{
    private readonly Func<int, string?> _resolve;

    public TestHostCodexExecutablePathProvider(Func<int, string?> resolve)
    {
        _resolve = resolve;
    }

    public string? TryGetExecutablePath(int processId) => _resolve(processId);
}

file sealed class TestAppServerSession : IHelperAppServerSession
{
    public event EventHandler<QuotaSnapshot>? RateLimitsUpdated;

    public int ReadRateLimitsCallCount { get; private set; }

    public Task<CodexAccountSummary> ReadAccountAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new CodexAccountSummary("chatgpt", "not-for-diagnostics@example.test", "pro", false));

    public Task<QuotaReadResult> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        ReadRateLimitsCallCount++;
        return Task.FromResult(new QuotaReadResult(
            new QuotaSnapshot(
                "codex",
                "Codex",
                new RateLimitWindow(37m, 300, DateTimeOffset.UtcNow.AddHours(2)),
                new RateLimitWindow(12m, 10080, DateTimeOffset.UtcNow.AddDays(4))),
            new Dictionary<string, QuotaSnapshot>()));
    }

    public void PublishQuota(QuotaSnapshot snapshot) => RateLimitsUpdated?.Invoke(this, snapshot);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class TestAutomaticTitleUpdateService : IAutomaticTitleUpdateService
{
    public List<AutomaticTitleUpdateRequest> Requests { get; } = [];

    public AutomaticTitleUpdateResult Apply(AutomaticTitleUpdateRequest request)
    {
        Requests.Add(request);
        return new AutomaticTitleUpdateResult(true, false, false, false, request.ExpectedCorrelation, 1, null, null);
    }
}

file sealed class TestHostWindowProvider : IHostWindowProvider
{
    public List<int> RequestedProcessIds { get; } = [];

    public TaskCompletionSource Enumerated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<WindowCandidate> ListTopLevelWindowsForProcess(int processId)
    {
        RequestedProcessIds.Add(processId);
        Enumerated.TrySetResult();
        return
        [
            new WindowCandidate(
                (nint)0x1234,
                (uint)processId,
                "ChatGPT.exe",
                "Chrome_WidgetWin_1",
                "Codex - Example Project",
                true,
                Array.Empty<CodexQuotaTitle.Processes.InspectedProcess>(),
                Array.Empty<int>()),
            new WindowCandidate(
                (nint)0x5678,
                (uint)processId,
                "ChatGPT.exe",
                "Chrome_WidgetWin_1",
                string.Empty,
                false,
                Array.Empty<CodexQuotaTitle.Processes.InspectedProcess>(),
                Array.Empty<int>()),
        ];
    }
}

file sealed class TestSingleWindowHostProvider : IHostWindowProvider
{
    private readonly TestTitleWriteWindow _window;

    public TestSingleWindowHostProvider(TestTitleWriteWindow window)
    {
        _window = window;
    }

    public IReadOnlyList<WindowCandidate> ListTopLevelWindowsForProcess(int processId) =>
    [
        new WindowCandidate(
            _window.Handle,
            (uint)processId,
            "ChatGPT.exe",
            AutomaticTitleUpdateService.ExpectedWindowClassName,
            _window.Title,
            true,
            Array.Empty<CodexQuotaTitle.Processes.InspectedProcess>(),
            Array.Empty<int>()),
    ];
}

file sealed class TestSettingsChangeWatcherFactory : ISettingsChangeWatcherFactory
{
    public ISettingsChangeWatcher Create(CodexQuotaTitleSettingsPaths paths) => new TestSettingsChangeWatcher();
}

file sealed class TestSettingsChangeWatcher : ISettingsChangeWatcher
{
    public Task WaitForChangeAsync(CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

    public void Dispose()
    {
    }
}

file sealed class TestTitleWriteWindow : ITitleWriteTestWindowAccessor, IWindowTitleAccessor
{
    public TestTitleWriteWindow(int processId, string title)
    {
        ProcessId = (uint)processId;
        Title = title;
    }

    public nint Handle { get; } = (nint)0x7654;

    public uint ProcessId { get; }

    public string Title { get; set; }

    public bool IsWindowResult { get; set; } = true;

    public int SetCallCount { get; private set; }

    public bool IsWindow(nint handle) => IsWindowResult && handle == Handle;

    public uint GetProcessId(nint handle) => ProcessId;

    public bool IsVisible(nint handle) => true;

    public string GetClassName(nint handle) => TitleWriteTestService.DefaultWindowClassName;

    public string GetWindowTitle(nint handle) => Title;

    public bool SetWindowTitle(nint handle, string title, out int win32Error)
    {
        SetCallCount++;
        win32Error = 0;
        Title = title;
        return true;
    }
}

file sealed class TestTitleWriteWindowCatalog : ITitleWriteTestWindowCatalog
{
    private readonly TestTitleWriteWindow _window;

    public TestTitleWriteWindowCatalog(TestTitleWriteWindow window)
    {
        _window = window;
    }

    public bool ReturnAmbiguousCandidates { get; set; }

    public IReadOnlyList<WindowCandidate> ListTopLevelWindowsForProcess(uint processId)
    {
        if (processId != _window.ProcessId)
        {
            return Array.Empty<WindowCandidate>();
        }

        var candidates = new List<WindowCandidate>
        {
            CreateCandidate(_window.Handle, _window.ProcessId, _window.Title),
        };
        if (ReturnAmbiguousCandidates)
        {
            candidates.Add(CreateCandidate((nint)0x7655, _window.ProcessId, "Codex - Other"));
        }

        return candidates;
    }

    private static WindowCandidate CreateCandidate(nint handle, uint processId, string title) =>
        new(
            handle,
            processId,
            "ChatGPT.exe",
            TitleWriteTestService.DefaultWindowClassName,
            title,
            true,
            Array.Empty<CodexQuotaTitle.Processes.InspectedProcess>(),
            Array.Empty<int>());
}

file sealed class CallbackTitleWriteTestDelay : ITitleWriteTestDelay
{
    private readonly Action _callback;

    public CallbackTitleWriteTestDelay(Action callback)
    {
        _callback = callback;
    }

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        _callback();
        return Task.CompletedTask;
    }
}

file sealed class TestTitleWriteLog : ITitleWriteTestLog
{
    public List<string> Markers { get; } = [];

    public void Write(string marker) => Markers.Add(marker);
}
