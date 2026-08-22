using System.Security.Cryptography;
using System.Text;

namespace CodexQuotaTitle.Profiles;

public sealed record ProfileIdentity(
    string? RawCodexHome,
    string NormalizedCodexHome,
    string ProfileHash,
    bool IsCodexHomeExplicit)
{
    public const int ProfileHashLength = 20;

    public static ProfileIdentity FromEnvironment() =>
        Create(new CodexHomeResolver().Resolve());

    public static ProfileIdentity Create(CodexHomeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (string.IsNullOrWhiteSpace(resolution.CodexHome))
        {
            throw new InvalidOperationException("CODEX_HOME could not be resolved.");
        }

        return CreateCore(
            resolution.CodexHome.Trim(),
            resolution.Source == CodexHomeSource.ExplicitEnvironment);
    }

    public static ProfileIdentity Create(string? codexHome)
    {
        var isExplicit = !string.IsNullOrWhiteSpace(codexHome);
        var rawCodexHome = isExplicit
            ? codexHome!.Trim()
            : new CodexHomeResolver().GetImplicitDefaultCodexHome() ?? throw new InvalidOperationException("The default CODEX_HOME could not be resolved.");

        return CreateCore(rawCodexHome, isExplicit);
    }

    public string MutexName => $"Local\\CodexQuotaTitle-{ProfileHash}";

    public string BootstrapMutexName => $"Local\\CodexQuotaTitle-Bootstrap-{ProfileHash}";

    private static ProfileIdentity CreateCore(string rawCodexHome, bool isExplicit)
    {
        var normalizedCodexHome = Normalize(rawCodexHome);
        var profileHash = CreateHash(normalizedCodexHome);

        return new ProfileIdentity(
            IsSafeForDiagnostics(rawCodexHome) ? rawCodexHome : null,
            normalizedCodexHome,
            profileHash,
            isExplicit);
    }

    private static string Normalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var withoutTrailingSeparator = Path.TrimEndingDirectorySeparator(fullPath);
        return OperatingSystem.IsWindows() ? withoutTrailingSeparator.ToUpperInvariant() : withoutTrailingSeparator;
    }

    private static string CreateHash(string normalizedCodexHome)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCodexHome));
        return Convert.ToHexString(bytes)[..ProfileHashLength].ToLowerInvariant();
    }

    private static bool IsSafeForDiagnostics(string value) =>
        !value.Contains("token", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("cookie", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("secret", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("apikey", StringComparison.OrdinalIgnoreCase);
}
