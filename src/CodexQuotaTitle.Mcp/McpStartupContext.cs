using CodexQuotaTitle.Profiles;

namespace CodexQuotaTitle.Mcp;

public sealed record McpStartupContext(CodexHomeResolution Resolution, ProfileIdentity? ProfileIdentity)
{
    public McpStartupContext(string? codexHome, ProfileIdentity? profileIdentity)
        : this(
            new CodexHomeResolution(
                codexHome,
                string.IsNullOrWhiteSpace(codexHome) ? null : global::CodexQuotaTitle.Profiles.CodexHomeSource.ExplicitEnvironment,
                !string.IsNullOrWhiteSpace(codexHome)),
            profileIdentity)
    {
    }

    public string? CodexHome => Resolution.CodexHome;

    public string? ExplicitCodexHome => Resolution.Source == global::CodexQuotaTitle.Profiles.CodexHomeSource.ExplicitEnvironment
        ? Resolution.CodexHome
        : null;

    public bool CodexHomeObserved => !string.IsNullOrWhiteSpace(ExplicitCodexHome);

    public bool CodexHomeAvailable => Resolution.IsAvailable;

    public string? CodexHomeSource => Resolution.DiagnosticSource;

    public static McpStartupContext FromEnvironment() =>
        FromResolver(new CodexHomeResolver());

    public static McpStartupContext FromResolver(ICodexHomeResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var resolution = resolver.Resolve();
        return resolution.IsAvailable
            ? new McpStartupContext(resolution, ProfileIdentity.Create(resolution))
            : new McpStartupContext(resolution, null);
    }
}
