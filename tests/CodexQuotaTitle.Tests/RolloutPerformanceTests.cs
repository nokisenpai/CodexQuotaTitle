using System.Text;
using CodexQuotaTitle.Sessions;

internal static class RolloutPerformanceTests
{
    public static void UnchangedHistoryIsNotReadAgain()
    {
        using var fixture = new Fixture();
        var padding = "{\"type\":\"response_item\",\"payload\":\"" + new string('x', 512 * 1024) + "\"}\n";
        long historyBytes = 0;
        var lastPath = string.Empty;
        for (var index = 0; index < 24; index++)
        {
            var path = fixture.Write($"history-{index}", padding + fixture.Quota(index, $"2026-08-23T{index:00}:00:00Z") + "\n");
            historyBytes += new FileInfo(path).Length;
            lastPath = path;
        }

        using var source = fixture.CreateSource();
        AssertQuota(23m, source.Refresh());
        AssertBytes(historyBytes, source);
        using var lockedHistory = new FileStream(lastPath, FileMode.Open, FileAccess.Read, FileShare.None);
        for (var refresh = 0; refresh < 20; refresh++)
        {
            AssertQuota(23m, source.Refresh());
        }

        AssertBytes(historyBytes, source);
        Console.WriteLine($"PROFILE_READ_BYTES initial={historyBytes} unchanged_refreshes=20 additional={source.ProfileBytesRead - historyBytes}");
    }

    public static void OnlyAppendedBytesAreRead()
    {
        using var fixture = new Fixture();
        fixture.Write("selected", fixture.Quota(20m, "2026-08-23T01:00:00Z") + "\n");
        var path = fixture.Write("other", "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":null}}\n");
        using var source = fixture.CreateSource();
        AssertQuota(20m, source.Refresh());
        var bytesBefore = source.ProfileBytesRead;
        var appended = fixture.Quota(61m, "2026-08-23T02:00:00Z");
        File.AppendAllText(path, appended, Encoding.UTF8);
        AssertQuota(20m, source.Refresh());
        AssertBytes(bytesBefore + Encoding.UTF8.GetByteCount(appended) + 64, source);

        File.AppendAllText(path, "\n", Encoding.UTF8);
        AssertQuota(61m, source.Refresh());
        AssertBytes(bytesBefore + Encoding.UTF8.GetByteCount(appended) + 1 + 128, source);
        var older = fixture.Quota(12m, "2026-08-23T00:00:00Z") + "\n{invalid json}\n";
        File.AppendAllText(path, older, Encoding.UTF8);
        AssertQuota(61m, source.Refresh());
        AssertBytes(bytesBefore + Encoding.UTF8.GetByteCount(appended + "\n" + older) + 192, source);
        Console.WriteLine($"PROFILE_READ_BYTES append={source.ProfileBytesRead - bytesBefore} expected={Encoding.UTF8.GetByteCount(appended + "\n" + older) + 192}");
    }

    public static void FileReplacementAndDeletionInvalidateCache()
    {
        using var fixture = new Fixture();
        var first = fixture.Write("first", fixture.Quota(20m, "2026-08-23T01:00:00Z") + "\n");
        var second = fixture.Write("second", fixture.Quota(61m, "2026-08-23T02:00:00Z") + "\n");
        using var source = fixture.CreateSource();
        AssertQuota(61m, source.Refresh());
        var bytesBefore = source.ProfileBytesRead;
        var replacement = fixture.Quota(73m, "2026-08-23T03:00:00Z") + "\n";
        File.WriteAllText(second, replacement, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddSeconds(1));
        AssertQuota(73m, source.Refresh());
        AssertBytes(bytesBefore + new FileInfo(second).Length, source);

        var largerReplacement = "{\"type\":\"response_item\",\"payload\":\"" + new string('y', 1024) + "\"}\n" +
            fixture.Quota(84m, "2026-08-23T04:00:00Z") + "\n";
        File.WriteAllText(second, largerReplacement, new UTF8Encoding(false));
        AssertQuota(84m, source.Refresh());

        File.WriteAllText(second, "{}\n", new UTF8Encoding(false));
        AssertQuota(20m, source.Refresh());
        var third = fixture.Write("third", fixture.Quota(88m, "2026-08-23T04:00:00Z") + "\n");
        AssertQuota(88m, source.Refresh());
        File.Delete(third);
        AssertQuota(20m, source.Refresh());
        File.Delete(first);
        var result = source.Refresh();
        if (result.Quotas is not null || result.Status != RolloutQuotaSourceStatuses.RolloutNotFound)
        {
            throw new InvalidOperationException("Deleted quota files remained in the profile cache.");
        }
    }

    public static void TimestampFallbackIsPreserved()
    {
        using var fixture = new Fixture();
        var withoutTimestamp = fixture.Quota(11m, null) + "\n";
        var withTimestamp = fixture.Quota(22m, "2026-08-23T02:00:00Z") + "\n";
        var path = fixture.Write("mixed", withoutTimestamp + withTimestamp);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 8, 23, 1, 0, 0, DateTimeKind.Utc));
        using var source = fixture.CreateSource();
        AssertQuota(22m, source.Refresh());
        var bytesBefore = source.ProfileBytesRead;
        File.AppendAllText(path, "{}\n", Encoding.UTF8);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 8, 23, 3, 0, 0, DateTimeKind.Utc));
        AssertQuota(11m, source.Refresh());
        AssertBytes(bytesBefore + 3 + 64, source);
    }

    private static void AssertQuota(decimal expected, RolloutQuotaReadResult result)
    {
        var actual = result.Quotas?.RateLimits?.Primary?.UsedPercent;
        if (actual != expected)
        {
            throw new InvalidOperationException($"Expected quota {expected}, actual {actual}, status {result.Status}.");
        }
    }

    private static void AssertBytes(long expected, RolloutQuotaSource source)
    {
        if (source.ProfileBytesRead != expected)
        {
            throw new InvalidOperationException($"Expected {expected} rollout bytes read, actual {source.ProfileBytesRead}.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "cqt-rollout-performance-tests");
        private readonly string _root;
        private readonly string _codexHome;
        private readonly ActiveSessionStore _store;

        public Fixture()
        {
            _root = Path.Combine(_testDirectory, Guid.NewGuid().ToString("N"));
            _codexHome = Path.Combine(_root, ".codex");
            _store = new ActiveSessionStore(Path.Combine(_root, "runtime"));
            Directory.CreateDirectory(Path.Combine(_codexHome, "sessions"));
        }

        public RolloutQuotaSource CreateSource() => new(_codexHome, _store);

        public string Write(string name, string events)
        {
            var path = Path.Combine(_codexHome, "sessions", name + ".jsonl");
            File.WriteAllText(path, "{\"type\":\"session_meta\",\"payload\":{\"id\":\"" + name + "\"}}\n" + events, new UTF8Encoding(false));
            return path;
        }

        public string Quota(decimal usedPercent, string? timestamp) =>
            (timestamp is null ? "{" : "{\"timestamp\":\"" + timestamp + "\",") +
            "\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":" +
            usedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"window_minutes\":10080,\"resets_at\":1788020771}}}}";

        public void Dispose()
        {
            var root = Path.GetFullPath(_root);
            if (!root.StartsWith(Path.GetFullPath(_testDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The fixture cleanup target is outside the test directory.");
            }

            Directory.Delete(root, recursive: true);
        }
    }
}
