using System.Buffers;
using System.Text;
using System.Text.Json;
using CodexQuotaTitle.Profiles;
using CodexQuotaTitle.Quotas;

namespace CodexQuotaTitle.Sessions;

public static class RolloutQuotaSourceStatuses
{
    public const string WaitingSession = "waiting-session";
    public const string RolloutNotFound = "rollout-not-found";
    public const string WatchingSession = "watching-session";
    public const string WatchingProfile = "watching-profile";
    public const string Watching = WatchingSession;
    public const string RateLimitsUnavailable = "rate-limits-unavailable";
    public const string Failed = "failed";
}

public static class RolloutQuotaSelections
{
    public const string ActiveSession = "active-session";
    public const string ProfileFallback = "profile-fallback";
}

public sealed record RolloutQuotaReadResult(
    string Status,
    string? ActiveSessionId,
    bool RolloutAvailable,
    QuotaReadResult? Quotas,
    DateTimeOffset? LastQuotaUpdatedAtUtc,
    string? FailureType = null,
    string? QuotaSelection = null);

public interface IRolloutQuotaSource : IDisposable
{
    RolloutQuotaReadResult Refresh();

    Task WaitForChangeAsync(CancellationToken cancellationToken);
}

public interface IRolloutQuotaSourceFactory
{
    IRolloutQuotaSource Create(ProfileIdentity profile);
}

public sealed class RolloutQuotaSourceFactory : IRolloutQuotaSourceFactory
{
    public IRolloutQuotaSource Create(ProfileIdentity profile) => new RolloutQuotaSource(profile);
}

public sealed class RolloutQuotaSource : IRolloutQuotaSource
{
    private const int BufferSize = 16 * 1024;
    private readonly string _sessionsDirectory;
    private readonly ActiveSessionStore _activeSessionStore;
    private readonly SemaphoreSlim _changes = new(0, 1);
    private readonly FileSystemWatcher _activeSessionWatcher;
    private FileSystemWatcher? _sessionsWatcher;
    private FileSystemWatcher? _rolloutWatcher;
    private string? _rolloutPath;
    private string? _activeSessionId;
    private string? _quotaSelection;
    private long _offset;
    private readonly ArrayBufferWriter<byte> _partialLine = new();
    private QuotaReadResult? _quotas;
    private DateTimeOffset? _lastQuotaUpdatedAtUtc;
    private bool _disposed;

    public RolloutQuotaSource(ProfileIdentity profile)
        : this(profile.NormalizedCodexHome, ActiveSessionStore.ForProfile(profile))
    {
    }

    public RolloutQuotaSource(string codexHome, ActiveSessionStore activeSessionStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codexHome);
        ArgumentNullException.ThrowIfNull(activeSessionStore);
        _sessionsDirectory = Path.Combine(codexHome, "sessions");
        _activeSessionStore = activeSessionStore;

        Directory.CreateDirectory(activeSessionStore.DirectoryPath);
        _activeSessionWatcher = CreateWatcher(
            activeSessionStore.DirectoryPath,
            Path.GetFileName(activeSessionStore.StatePath),
            includeSubdirectories: false);
        EnsureSessionsWatcher();
    }

    public RolloutQuotaReadResult Refresh()
    {
        ThrowIfDisposed();
        EnsureSessionsWatcher();
        var activeSession = _activeSessionStore.TryRead();
        if (activeSession is not null)
        {
            var activeRolloutPath = ResolveRollout(activeSession);
            if (activeRolloutPath is not null)
            {
                return RefreshSelectedRollout(
                    activeRolloutPath,
                    activeSession.SessionId,
                    RolloutQuotaSelections.ActiveSession);
            }
        }

        var profileRollout = ResolveLatestProfileRollout();
        if (profileRollout is null)
        {
            ResetRollout();
            return new RolloutQuotaReadResult(
                RolloutQuotaSourceStatuses.RolloutNotFound,
                null,
                false,
                null,
                null,
                QuotaSelection: RolloutQuotaSelections.ProfileFallback);
        }

        return RefreshSelectedRollout(profileRollout.Path, null, RolloutQuotaSelections.ProfileFallback);
    }

    private RolloutQuotaReadResult RefreshSelectedRollout(string resolvedPath, string? activeSessionId, string quotaSelection)
    {
        try
        {
            if (!string.Equals(_rolloutPath, resolvedPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_activeSessionId, activeSessionId, StringComparison.Ordinal) ||
                !string.Equals(_quotaSelection, quotaSelection, StringComparison.Ordinal))
            {
                SwitchRollout(resolvedPath, activeSessionId, quotaSelection);
            }

            ReadAvailableBytes();
            var watchingStatus = string.Equals(quotaSelection, RolloutQuotaSelections.ActiveSession, StringComparison.Ordinal)
                ? RolloutQuotaSourceStatuses.WatchingSession
                : RolloutQuotaSourceStatuses.WatchingProfile;
            return _quotas is null
                ? new RolloutQuotaReadResult(
                    RolloutQuotaSourceStatuses.RateLimitsUnavailable,
                    _activeSessionId,
                    true,
                    null,
                    null,
                    QuotaSelection: _quotaSelection)
                : new RolloutQuotaReadResult(
                    watchingStatus,
                    _activeSessionId,
                    true,
                    _quotas,
                    _lastQuotaUpdatedAtUtc,
                    QuotaSelection: _quotaSelection);
        }
        catch (IOException)
        {
            return new RolloutQuotaReadResult(
                RolloutQuotaSourceStatuses.Failed,
                activeSessionId,
                _rolloutPath is not null,
                _quotas,
                _lastQuotaUpdatedAtUtc,
                "IOException",
                _quotaSelection);
        }
        catch (UnauthorizedAccessException)
        {
            return new RolloutQuotaReadResult(
                RolloutQuotaSourceStatuses.Failed,
                activeSessionId,
                _rolloutPath is not null,
                _quotas,
                _lastQuotaUpdatedAtUtc,
                "UnauthorizedAccessException",
                _quotaSelection);
        }
        catch (JsonException)
        {
            return new RolloutQuotaReadResult(
                RolloutQuotaSourceStatuses.Failed,
                activeSessionId,
                _rolloutPath is not null,
                _quotas,
                _lastQuotaUpdatedAtUtc,
                "JsonException",
                _quotaSelection);
        }
    }

    public Task WaitForChangeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _changes.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeSessionWatcher.Dispose();
        _sessionsWatcher?.Dispose();
        _rolloutWatcher?.Dispose();
        _changes.Dispose();
    }

    private string? ResolveRollout(ActiveSessionState activeSession)
    {
        if (IsValidRollout(activeSession.TranscriptPath, activeSession.SessionId))
        {
            return activeSession.TranscriptPath;
        }

        if (!Directory.Exists(_sessionsDirectory))
        {
            return null;
        }

        try
        {
            foreach (var candidate in Directory.EnumerateFiles(_sessionsDirectory, "*.jsonl", SearchOption.AllDirectories))
            {
                if (IsValidRollout(candidate, activeSession.SessionId))
                {
                    return candidate;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private ProfileRolloutCandidate? ResolveLatestProfileRollout()
    {
        if (!Directory.Exists(_sessionsDirectory))
        {
            return null;
        }

        try
        {
            var candidates = Directory
                .EnumerateFiles(_sessionsDirectory, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase);

            ProfileRolloutCandidate? latest = null;
            foreach (var candidate in candidates)
            {
                if (!TryReadLatestRateLimits(candidate, out var snapshot) || snapshot is null)
                {
                    continue;
                }

                if (latest is null || snapshot.IsNewerThan(latest))
                {
                    latest = snapshot;
                }
            }

            return latest;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryReadLatestRateLimits(FileInfo file, out ProfileRolloutCandidate? latest)
    {
        latest = null;
        try
        {
            var lastWriteTimeUtc = new DateTimeOffset(file.LastWriteTimeUtc);
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, BufferSize, leaveOpen: false);
            var lineNumber = 0L;
            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                try
                {
                    if (!TryReadRateLimits(Encoding.UTF8.GetBytes(line), out var quotas, out var timestamp))
                    {
                        continue;
                    }

                    var snapshot = new ProfileRolloutCandidate(
                        file.FullName,
                        timestamp ?? lastWriteTimeUtc,
                        lastWriteTimeUtc,
                        lineNumber);
                    if (latest is null || snapshot.IsNewerThan(latest))
                    {
                        latest = snapshot;
                    }
                }
                catch (JsonException)
                {
                    // A partially written JSONL line is ignored until the next watcher or resync pass.
                }
            }

            return latest is not null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsValidRollout(string? path, string expectedSessionId)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, BufferSize, leaveOpen: false);
            while (reader.ReadLine() is { } line)
            {
                if (TryReadSessionMetaId(Encoding.UTF8.GetBytes(line), out var actualSessionId))
                {
                    return string.Equals(actualSessionId, expectedSessionId, StringComparison.Ordinal);
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private void SwitchRollout(string path, string? activeSessionId, string quotaSelection)
    {
        _rolloutWatcher?.Dispose();
        _rolloutWatcher = CreateWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path), includeSubdirectories: false);
        _rolloutPath = path;
        _activeSessionId = activeSessionId;
        _quotaSelection = quotaSelection;
        _offset = 0;
        _partialLine.Clear();
        _quotas = null;
        _lastQuotaUpdatedAtUtc = null;
    }

    private void ResetRollout()
    {
        _rolloutWatcher?.Dispose();
        _rolloutWatcher = null;
        _rolloutPath = null;
        _activeSessionId = null;
        _quotaSelection = null;
        _offset = 0;
        _partialLine.Clear();
        _quotas = null;
        _lastQuotaUpdatedAtUtc = null;
    }

    private void ReadAvailableBytes()
    {
        if (_rolloutPath is null)
        {
            return;
        }

        using var stream = new FileStream(_rolloutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < _offset)
        {
            _offset = 0;
            _partialLine.Clear();
            _quotas = null;
            _lastQuotaUpdatedAtUtc = null;
        }

        stream.Position = _offset;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ProcessBytes(buffer.AsSpan(0, bytesRead));
                _offset += bytesRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ProcessBytes(ReadOnlySpan<byte> bytes)
    {
        var start = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'\n')
            {
                continue;
            }

            var line = bytes[start..index];
            if (!line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (_partialLine.WrittenCount > 0)
            {
                _partialLine.Write(line);
                ProcessCompleteLine(_partialLine.WrittenSpan);
                _partialLine.Clear();
            }
            else
            {
                ProcessCompleteLine(line);
            }

            start = index + 1;
        }

        if (start < bytes.Length)
        {
            _partialLine.Write(bytes[start..]);
        }
    }

    private void ProcessCompleteLine(ReadOnlySpan<byte> line)
    {
        line = TrimUtf8Bom(line);
        try
        {
            if (TryReadRateLimits(line, out var quotas, out var timestamp))
            {
                _quotas = quotas;
                _lastQuotaUpdatedAtUtc = timestamp ?? DateTimeOffset.UtcNow;
            }
        }
        catch (JsonException)
        {
            // A partially written JSONL line is ignored until a complete update is available.
        }
    }

    private static bool TryReadSessionMetaId(ReadOnlySpan<byte> line, out string? sessionId)
    {
        line = TrimUtf8Bom(line);
        sessionId = null;
        var reader = new Utf8JsonReader(line, isFinalBlock: true, state: default);
        string? eventType = null;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                return false;
            }

            if (string.Equals(propertyName, "type", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.String)
            {
                eventType = reader.GetString();
            }
            else if (string.Equals(propertyName, "payload", StringComparison.Ordinal) &&
                     string.Equals(eventType, "session_meta", StringComparison.Ordinal) &&
                     reader.TokenType == JsonTokenType.StartObject)
            {
                sessionId = ReadStringProperty(ref reader, "id");
                return !string.IsNullOrWhiteSpace(sessionId);
            }
            else
            {
                reader.Skip();
            }
        }

        return false;
    }

    private static bool TryReadRateLimits(ReadOnlySpan<byte> line, out QuotaReadResult quotas, out DateTimeOffset? timestamp)
    {
        line = TrimUtf8Bom(line);
        quotas = default!;
        timestamp = null;
        var reader = new Utf8JsonReader(line, isFinalBlock: true, state: default);
        string? eventType = null;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                return false;
            }

            if (string.Equals(propertyName, "type", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.String)
            {
                eventType = reader.GetString();
            }
            else if (string.Equals(propertyName, "timestamp", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.String)
            {
                if (DateTimeOffset.TryParse(reader.GetString(), out var parsedTimestamp))
                {
                    timestamp = parsedTimestamp;
                }
            }
            else if (string.Equals(propertyName, "payload", StringComparison.Ordinal) &&
                     string.Equals(eventType, "event_msg", StringComparison.Ordinal) &&
                     reader.TokenType == JsonTokenType.StartObject)
            {
                return TryReadTokenCountPayload(ref reader, out quotas);
            }
            else
            {
                reader.Skip();
            }
        }

        return false;
    }

    private static bool TryReadTokenCountPayload(ref Utf8JsonReader reader, out QuotaReadResult quotas)
    {
        quotas = default!;
        string? payloadType = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                return false;
            }

            if (string.Equals(propertyName, "type", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.String)
            {
                payloadType = reader.GetString();
            }
            else if (string.Equals(propertyName, "rate_limits", StringComparison.Ordinal) &&
                     string.Equals(payloadType, "token_count", StringComparison.Ordinal) &&
                     reader.TokenType == JsonTokenType.StartObject)
            {
                return TryReadRateLimits(ref reader, out quotas);
            }
            else
            {
                reader.Skip();
            }
        }

        return false;
    }

    private static bool TryReadRateLimits(ref Utf8JsonReader reader, out QuotaReadResult quotas)
    {
        quotas = default!;
        string? limitId = null;
        string? limitName = null;
        RateLimitWindow? primary = null;
        RateLimitWindow? secondary = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                return false;
            }

            switch (propertyName)
            {
                case "limit_id" when reader.TokenType == JsonTokenType.String:
                    limitId = reader.GetString();
                    break;
                case "limit_name" when reader.TokenType == JsonTokenType.String:
                    limitName = reader.GetString();
                    break;
                case "primary" when reader.TokenType == JsonTokenType.StartObject:
                    primary = TryReadWindow(ref reader);
                    break;
                case "secondary" when reader.TokenType == JsonTokenType.StartObject:
                    secondary = TryReadWindow(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (primary is null && secondary is null)
        {
            return false;
        }

        var snapshot = new QuotaSnapshot(limitId, limitName, primary, secondary);
        quotas = new QuotaReadResult(snapshot, new Dictionary<string, QuotaSnapshot>());
        return true;
    }

    private static RateLimitWindow? TryReadWindow(ref Utf8JsonReader reader)
    {
        decimal? usedPercent = null;
        int? windowMinutes = null;
        long? resetsAt = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                return null;
            }

            if (string.Equals(propertyName, "used_percent", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.Number)
            {
                usedPercent = reader.GetDecimal();
            }
            else if (string.Equals(propertyName, "window_minutes", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.Number)
            {
                windowMinutes = reader.GetInt32();
            }
            else if (string.Equals(propertyName, "resets_at", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.Number)
            {
                resetsAt = reader.GetInt64();
            }
            else
            {
                reader.Skip();
            }
        }

        return usedPercent is null ? null : new RateLimitWindow(usedPercent.Value, windowMinutes, ToDateTimeOffset(resetsAt));
    }

    private static string? ReadStringProperty(ref Utf8JsonReader reader, string expectedPropertyName)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                return null;
            }

            if (string.Equals(propertyName, expectedPropertyName, StringComparison.Ordinal) && reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            reader.Skip();
        }

        return null;
    }

    private static DateTimeOffset? ToDateTimeOffset(long? unixTimestamp)
    {
        if (unixTimestamp is null)
        {
            return null;
        }

        return unixTimestamp.Value > 10_000_000_000L
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixTimestamp.Value)
            : DateTimeOffset.FromUnixTimeSeconds(unixTimestamp.Value);
    }

    private static ReadOnlySpan<byte> TrimUtf8Bom(ReadOnlySpan<byte> value) =>
        value.Length >= 3 && value[0] == 0xEF && value[1] == 0xBB && value[2] == 0xBF
            ? value[3..]
            : value;

    private void EnsureSessionsWatcher()
    {
        if (_sessionsWatcher is null && Directory.Exists(_sessionsDirectory))
        {
            _sessionsWatcher = CreateWatcher(_sessionsDirectory, "*.jsonl", includeSubdirectories: true);
        }
    }

    private FileSystemWatcher CreateWatcher(string directoryPath, string filter, bool includeSubdirectories)
    {
        var watcher = new FileSystemWatcher(directoryPath, filter)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        watcher.Changed += OnWatcherChanged;
        watcher.Created += OnWatcherChanged;
        watcher.Deleted += OnWatcherChanged;
        watcher.Renamed += OnWatcherRenamed;
        watcher.Error += OnWatcherError;
        return watcher;
    }

    private void OnWatcherChanged(object? sender, FileSystemEventArgs args) => SignalChange();

    private void OnWatcherRenamed(object? sender, RenamedEventArgs args) => SignalChange();

    private void OnWatcherError(object? sender, ErrorEventArgs args) => SignalChange();

    private void SignalChange()
    {
        try
        {
            _changes.Release();
        }
        catch (SemaphoreFullException)
        {
            // A subsequent refresh sees the latest session state and file length.
        }
        catch (ObjectDisposedException)
        {
            // The source is shutting down.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record ProfileRolloutCandidate(
        string Path,
        DateTimeOffset ObservedAtUtc,
        DateTimeOffset LastWriteTimeUtc,
        long LineNumber)
    {
        public bool IsNewerThan(ProfileRolloutCandidate other)
        {
            var observedComparison = ObservedAtUtc.CompareTo(other.ObservedAtUtc);
            if (observedComparison != 0)
            {
                return observedComparison > 0;
            }

            var writeComparison = LastWriteTimeUtc.CompareTo(other.LastWriteTimeUtc);
            if (writeComparison != 0)
            {
                return writeComparison > 0;
            }

            var lineComparison = LineNumber.CompareTo(other.LineNumber);
            return lineComparison != 0
                ? lineComparison > 0
                : string.Compare(Path, other.Path, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }
}
