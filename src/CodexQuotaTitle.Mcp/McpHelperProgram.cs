using CodexQuotaTitle.Profiles;

namespace CodexQuotaTitle.Mcp;

public static class McpHelperProgram
{
    public static async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        HelperRuntimePaths? runtimePaths = null;
        try
        {
            var codexHome = GetRequiredOption(arguments, "--codex-home");
            var suppliedProfileIdentity = GetRequiredOption(arguments, "--profile-identity");
            var hostChatGptPid = GetRequiredPositiveIntOption(arguments, "--host-chatgpt-pid");
            var hostCodexPid = GetOptionalPositiveIntOption(arguments, "--host-codex-pid");
            var origin = GetOptionalOption(arguments, "--origin") ?? "mcp";
            var profile = ProfileIdentity.Create(codexHome);
            if (!string.Equals(profile.ProfileHash, suppliedProfileIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The supplied ProfileIdentity does not match CODEX_HOME.");
            }

            runtimePaths = new HelperRuntimePaths(GetOptionalOption(arguments, "--runtime-directory") ?? HelperRuntimePaths.ForProfile(profile.ProfileHash).DirectoryPath);
            var request = new McpHelperStartupRequest(
                codexHome,
                profile,
                hostChatGptPid,
                hostCodexPid,
                origin,
                runtimePaths);
            await new McpHelperService().RunAsync(request, cancellationToken);
            return 0;
        }
        catch (Exception exception)
        {
            (runtimePaths is null ? new HelperServiceLog(Path.Combine(Path.GetTempPath(), "CodexQuotaTitle")) : new HelperServiceLog(runtimePaths.DirectoryPath))
                .AppendException("HELPER_SERVICE_FAILED", exception);
            return 1;
        }
    }

    private static string GetRequiredOption(IReadOnlyList<string> arguments, string name) =>
        GetOptionalOption(arguments, name) ?? throw new ArgumentException($"Missing required option '{name}'.");

    private static int GetRequiredPositiveIntOption(IReadOnlyList<string> arguments, string name)
    {
        var value = GetRequiredOption(arguments, name);
        return int.TryParse(value, out var result) && result > 0
            ? result
            : throw new ArgumentException($"Option '{name}' must be a positive process identifier.");
    }

    private static int? GetOptionalPositiveIntOption(IReadOnlyList<string> arguments, string name)
    {
        var value = GetOptionalOption(arguments, name);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, out var result) && result > 0
            ? result
            : throw new ArgumentException($"Option '{name}' must be a positive process identifier.");
    }

    private static string? GetOptionalOption(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
