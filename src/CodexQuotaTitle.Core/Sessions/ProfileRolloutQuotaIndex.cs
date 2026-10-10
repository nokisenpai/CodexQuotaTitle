using System.Buffers;
using System.Text.Json;
using CodexQuotaTitle.Quotas;

namespace CodexQuotaTitle.Sessions;

internal sealed class ProfileRolloutQuotaIndex(string sessionsDirectory)
{
    private readonly Dictionary<string, IncrementalRolloutReader> _readers = new(StringComparer.OrdinalIgnoreCase);

    internal long BytesRead { get; private set; }

    public ProfileRolloutSnapshot? Refresh()
    {
        if (!Directory.Exists(sessionsDirectory))
        {
            _readers.Clear();
            return null;
        }

        try
        {
            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ProfileRolloutSnapshot? latest = null;
            foreach (var path in Directory.EnumerateFiles(sessionsDirectory, "*.jsonl", SearchOption.AllDirectories))
            {
                existingPaths.Add(path);
                if (!_readers.TryGetValue(path, out var reader))
                {
                    reader = new IncrementalRolloutReader();
                    _readers.Add(path, reader);
                }

                var bytesBefore = reader.BytesRead;
                ProfileRolloutSnapshot? snapshot;
                try
                {
                    snapshot = reader.Refresh(new FileInfo(path));
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                finally
                {
                    BytesRead += reader.BytesRead - bytesBefore;
                }

                if (snapshot is not null && (latest is null || snapshot.IsNewerThan(latest)))
                {
                    latest = snapshot;
                }
            }

            foreach (var path in _readers.Keys.Where(path => !existingPaths.Contains(path)).ToArray())
            {
                _readers.Remove(path);
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

    private sealed class IncrementalRolloutReader
    {
        private const int BufferSize = 16 * 1024;
        private readonly byte[] _tail = new byte[64];
        private int _tailCount;
        private ArrayBufferWriter<byte> _partialLine = new();
        private long _offset;
        private long _lineNumber;
        private DateTime _lastWriteTimeUtc;
        private DateTime _creationTimeUtc;
        private bool _initialized;
        private ProfileRolloutSnapshot? _timestamped;
        private ProfileRolloutSnapshot? _untimestamped;

        public long BytesRead { get; private set; }

        public ProfileRolloutSnapshot? Refresh(FileInfo file)
        {
            var length = file.Length;
            var lastWriteTimeUtc = file.LastWriteTimeUtc;
            var creationTimeUtc = file.CreationTimeUtc;
            if (!_initialized || length < _offset || creationTimeUtc != _creationTimeUtc ||
                (length == _offset && lastWriteTimeUtc != _lastWriteTimeUtc))
            {
                Reset();
            }

            var lastWrite = new DateTimeOffset(lastWriteTimeUtc);
            _timestamped = _timestamped is null ? null : _timestamped with { LastWriteTimeUtc = lastWrite };
            _untimestamped = _untimestamped is null ? null : _untimestamped with { LastWriteTimeUtc = lastWrite };
            if (length > _offset)
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (_tailCount > 0)
                {
                    // A growing file can also be a rewrite. Verify a bounded tail before treating it as an append.
                    stream.Position = _offset - _tailCount;
                    Span<byte> previousTail = stackalloc byte[64];
                    stream.ReadExactly(previousTail[.._tailCount]);
                    BytesRead += _tailCount;
                    if (!previousTail[.._tailCount].SequenceEqual(_tail.AsSpan(0, _tailCount)))
                    {
                        Reset();
                    }
                }

                stream.Position = _offset;
                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    // Read only the length observed before opening; a later append is picked up on the next refresh.
                    while (_offset < length)
                    {
                        var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length - _offset));
                        if (count == 0)
                        {
                            break;
                        }

                        BytesRead += count;
                        ProcessBytes(buffer.AsSpan(0, count), file.FullName, lastWrite);
                        RememberTail(buffer.AsSpan(0, count));
                        _offset += count;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            _lastWriteTimeUtc = lastWriteTimeUtc;
            _creationTimeUtc = creationTimeUtc;
            _initialized = true;
            return _timestamped is null || (_untimestamped is not null && _untimestamped.IsNewerThan(_timestamped))
                ? _untimestamped
                : _timestamped;
        }

        private void Reset()
        {
            _offset = 0;
            _lineNumber = 0;
            _tailCount = 0;
            _partialLine = new ArrayBufferWriter<byte>();
            _timestamped = null;
            _untimestamped = null;
        }

        private void RememberTail(ReadOnlySpan<byte> bytes)
        {
            var added = Math.Min(bytes.Length, _tail.Length);
            var retained = Math.Min(_tailCount, _tail.Length - added);
            _tail.AsSpan(_tailCount - retained, retained).CopyTo(_tail);
            bytes[^added..].CopyTo(_tail.AsSpan(retained));
            _tailCount = retained + added;
        }

        private void ProcessBytes(ReadOnlySpan<byte> bytes, string path, DateTimeOffset lastWrite)
        {
            var start = 0;
            for (var index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] != (byte)'\n')
                {
                    continue;
                }

                var line = bytes[start..index];
                if (_partialLine.WrittenCount > 0)
                {
                    _partialLine.Write(line);
                    ProcessLine(_partialLine.WrittenSpan, path, lastWrite);
                    // Do not retain large conversation-line buffers for every historical rollout.
                    _partialLine = _partialLine.Capacity > BufferSize ? new ArrayBufferWriter<byte>() : _partialLine;
                    _partialLine.Clear();
                }
                else
                {
                    ProcessLine(line, path, lastWrite);
                }

                start = index + 1;
            }

            if (start < bytes.Length)
            {
                _partialLine.Write(bytes[start..]);
            }
        }

        private void ProcessLine(ReadOnlySpan<byte> line, string path, DateTimeOffset lastWrite)
        {
            _lineNumber++;
            try
            {
                if (!RolloutQuotaSource.TryReadRateLimits(line, out var quotas, out var timestamp))
                {
                    return;
                }

                var snapshot = new ProfileRolloutSnapshot(path, quotas, timestamp, lastWrite, _lineNumber);
                if (timestamp is null)
                {
                    _untimestamped = snapshot;
                }
                else if (_timestamped is null || snapshot.IsNewerThan(_timestamped))
                {
                    _timestamped = snapshot;
                }
            }
            catch (JsonException)
            {
                // Malformed complete lines must not discard the last valid quota snapshot.
            }
        }
    }
}

internal sealed record ProfileRolloutSnapshot(
    string Path,
    QuotaReadResult Quotas,
    DateTimeOffset? Timestamp,
    DateTimeOffset LastWriteTimeUtc,
    long LineNumber)
{
    public DateTimeOffset ObservedAtUtc => Timestamp ?? LastWriteTimeUtc;

    public bool IsNewerThan(ProfileRolloutSnapshot other)
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
