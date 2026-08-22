using System.Text.Json;
using CodexQuotaTitle.Profiles;
using CodexQuotaTitle.Titles;

namespace CodexQuotaTitle.Settings;

public sealed record CodexQuotaTitleSettings
{
    public const string DefaultTemplate = "%base_title% | W %week_percent_remaining% • %week_reset_date%";
    public const string DefaultDateFormat = "dd/MM HH:mm";

    public string Template { get; init; } = DefaultTemplate;

    public string DateFormat { get; init; } = DefaultDateFormat;

    public bool Enabled { get; init; } = true;

    public static CodexQuotaTitleSettings Defaults { get; } = new();
}

public sealed record CodexQuotaTitleSettingsPaths(string DirectoryPath)
{
    public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
}

public interface ICodexQuotaTitleSettingsPathResolver
{
    CodexQuotaTitleSettingsPaths GetPaths(ProfileIdentity profile);
}

public sealed class LocalAppDataSettingsPathResolver : ICodexQuotaTitleSettingsPathResolver
{
    private readonly string _rootDirectory;

    public LocalAppDataSettingsPathResolver(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ??
            Environment.GetEnvironmentVariable("CODEXQUOTATITLE_SETTINGS_ROOT") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaTitle");
    }

    public CodexQuotaTitleSettingsPaths GetPaths(ProfileIdentity profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new CodexQuotaTitleSettingsPaths(Path.Combine(_rootDirectory, profile.ProfileHash));
    }
}

public sealed record CodexQuotaTitleSettingsLoadResult(
    CodexQuotaTitleSettings Settings,
    bool UsedDefaults,
    string? Error);

public interface ICodexQuotaTitleSettingsStoreFactory
{
    CodexQuotaTitleSettingsStore Create(ProfileIdentity profile);
}

public sealed class CodexQuotaTitleSettingsStoreFactory : ICodexQuotaTitleSettingsStoreFactory
{
    private readonly ICodexQuotaTitleSettingsPathResolver _pathResolver;

    public CodexQuotaTitleSettingsStoreFactory(ICodexQuotaTitleSettingsPathResolver? pathResolver = null)
    {
        _pathResolver = pathResolver ?? new LocalAppDataSettingsPathResolver();
    }

    public CodexQuotaTitleSettingsStore Create(ProfileIdentity profile) => new(_pathResolver.GetPaths(profile));
}

public sealed class CodexQuotaTitleSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    private readonly CodexQuotaTitleSettingsPaths _paths;

    public CodexQuotaTitleSettingsStore(CodexQuotaTitleSettingsPaths paths)
    {
        _paths = paths;
    }

    public CodexQuotaTitleSettingsPaths Paths => _paths;

    public CodexQuotaTitleSettingsLoadResult LoadOrCreate()
    {
        if (!File.Exists(_paths.SettingsPath))
        {
            try
            {
                SaveDefaultSettings(CodexQuotaTitleSettings.Defaults);
                return new CodexQuotaTitleSettingsLoadResult(CodexQuotaTitleSettings.Defaults, true, null);
            }
            catch (IOException exception)
            {
                return new CodexQuotaTitleSettingsLoadResult(CodexQuotaTitleSettings.Defaults, true, exception.GetType().Name);
            }
            catch (UnauthorizedAccessException exception)
            {
                return new CodexQuotaTitleSettingsLoadResult(CodexQuotaTitleSettings.Defaults, true, exception.GetType().Name);
            }
        }

        try
        {
            var settings = JsonSerializer.Deserialize<CodexQuotaTitleSettings>(File.ReadAllText(_paths.SettingsPath), SerializerOptions);
            if (settings is null || !IsValid(settings))
            {
                return new CodexQuotaTitleSettingsLoadResult(CodexQuotaTitleSettings.Defaults, true, "settings-json-invalid");
            }

            return new CodexQuotaTitleSettingsLoadResult(settings, false, null);
        }
        catch (IOException exception)
        {
            return new CodexQuotaTitleSettingsLoadResult(CodexQuotaTitleSettings.Defaults, true, exception.GetType().Name);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new CodexQuotaTitleSettingsLoadResult(CodexQuotaTitleSettings.Defaults, true, exception.GetType().Name);
        }
        catch (JsonException)
        {
            return new CodexQuotaTitleSettingsLoadResult(CodexQuotaTitleSettings.Defaults, true, "settings-json-invalid");
        }
    }

    public void Save(CodexQuotaTitleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WriteAtomically(JsonSerializer.Serialize(settings, SerializerOptions));
    }

    public void UpdateTemplatePreservingJsonc(string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        var contents = File.ReadAllText(_paths.SettingsPath);
        CodexQuotaTitleSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<CodexQuotaTitleSettings>(contents, SerializerOptions);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("settings-json-invalid");
        }

        if (!IsValid(settings))
        {
            throw new InvalidDataException("settings-json-invalid");
        }

        WriteAtomically(JsoncTopLevelStringPropertyUpdater.ReplaceStringValue(contents, "template", template));
    }

    private void SaveDefaultSettings(CodexQuotaTitleSettings settings)
    {
        var placeholderComments = string.Join(
            Environment.NewLine,
            QuotaTemplateRenderer.SupportedPlaceholders.Select(placeholder => string.Concat("// ", placeholder)));
        var contents = string.Concat(
            "// CodexQuotaTitle supported placeholders:",
            Environment.NewLine,
            placeholderComments,
            Environment.NewLine,
            Environment.NewLine,
            JsonSerializer.Serialize(settings, SerializerOptions));
        WriteAtomically(contents);
    }

    private void WriteAtomically(string contents)
    {
        Directory.CreateDirectory(_paths.DirectoryPath);
        var temporaryPath = string.Concat(_paths.SettingsPath, ".", Guid.NewGuid().ToString("N"), ".tmp");
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, _paths.SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsValid(CodexQuotaTitleSettings? settings) =>
        settings is not null &&
        !string.IsNullOrWhiteSpace(settings.Template) &&
        !string.IsNullOrWhiteSpace(settings.DateFormat);
}
