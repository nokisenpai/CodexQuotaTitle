using System.Text.Json;
using CodexQuotaTitle.Profiles;

namespace CodexQuotaTitle.Sessions;

public sealed record ActiveSessionState(
    string SessionId,
    string? TranscriptPath,
    DateTimeOffset UpdatedAtUtc);

public sealed class ActiveSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _directoryPath;

    public ActiveSessionStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = directoryPath;
    }

    public string DirectoryPath => _directoryPath;

    public string StatePath => Path.Combine(_directoryPath, "active-session.json");

    public static ActiveSessionStore ForProfile(ProfileIdentity profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new ActiveSessionStore(ProfileRuntimeDirectory.ForProfile(profile.ProfileHash));
    }

    public bool Update(string? sessionId, string? transcriptPath, DateTimeOffset updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        Directory.CreateDirectory(_directoryPath);
        var state = new ActiveSessionState(sessionId.Trim(), NormalizePath(transcriptPath), updatedAtUtc);
        var temporaryPath = string.Concat(StatePath, ".", Guid.NewGuid().ToString("N"), ".tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, SerializerOptions));
            File.Move(temporaryPath, StatePath, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public ActiveSessionState? TryRead()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<ActiveSessionState>(File.ReadAllText(StatePath), SerializerOptions);
            return state is null || string.IsNullOrWhiteSpace(state.SessionId)
                ? null
                : state with { SessionId = state.SessionId.Trim(), TranscriptPath = NormalizePath(state.TranscriptPath) };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizePath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ProfileRuntimeDirectory
{
    public static string ForProfile(string profileHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileHash);
        return Path.Combine(Path.GetTempPath(), "CodexQuotaTitle", "runtime", profileHash);
    }
}
