namespace CodexQuotaTitle.Profiles;

public enum CodexHomeSource
{
    ExplicitEnvironment,
    ImplicitDefault,
}

public sealed record CodexHomeResolution(string? CodexHome, CodexHomeSource? Source, bool Exists)
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(CodexHome) && Exists;

    public string? DiagnosticSource => Source switch
    {
        CodexHomeSource.ExplicitEnvironment => "EXPLICIT",
        CodexHomeSource.ImplicitDefault => "IMPLICIT_DEFAULT",
        _ => null,
    };
}

public interface ICodexHomeResolver
{
    CodexHomeResolution Resolve();
}

public sealed class CodexHomeResolver : ICodexHomeResolver
{
    private readonly Func<string?> _readCodexHomeEnvironment;
    private readonly Func<string> _getUserProfileDirectory;
    private readonly Func<string, bool> _directoryExists;

    public CodexHomeResolver(
        Func<string?>? readCodexHomeEnvironment = null,
        Func<string>? getUserProfileDirectory = null,
        Func<string, bool>? directoryExists = null)
    {
        _readCodexHomeEnvironment = readCodexHomeEnvironment ?? (() => Environment.GetEnvironmentVariable("CODEX_HOME"));
        _getUserProfileDirectory = getUserProfileDirectory ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public CodexHomeResolution Resolve()
    {
        var explicitCodexHome = _readCodexHomeEnvironment();
        if (!string.IsNullOrWhiteSpace(explicitCodexHome))
        {
            return CreateResolution(explicitCodexHome.Trim(), CodexHomeSource.ExplicitEnvironment);
        }

        return CreateResolution(GetImplicitDefaultCodexHome(), CodexHomeSource.ImplicitDefault);
    }

    public string? GetImplicitDefaultCodexHome()
    {
        var userProfileDirectory = _getUserProfileDirectory();
        return string.IsNullOrWhiteSpace(userProfileDirectory)
            ? null
            : Path.Combine(userProfileDirectory, ".codex");
    }

    private CodexHomeResolution CreateResolution(string? codexHome, CodexHomeSource source) =>
        new(codexHome, source, !string.IsNullOrWhiteSpace(codexHome) && _directoryExists(codexHome));
}
