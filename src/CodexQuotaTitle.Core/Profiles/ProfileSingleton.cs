using System.Collections.Concurrent;

namespace CodexQuotaTitle.Profiles;

public sealed class ProfileSingleton : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> OwnedProfilesInCurrentProcess = new(StringComparer.Ordinal);
    private readonly Semaphore? _semaphore;
    private readonly string _synchronizationName;
    private readonly bool _registeredInCurrentProcess;
    private bool _ownsSemaphore;

    private ProfileSingleton(Semaphore? semaphore, string synchronizationName, bool ownsSemaphore, bool registeredInCurrentProcess)
    {
        _semaphore = semaphore;
        _synchronizationName = synchronizationName;
        _ownsSemaphore = ownsSemaphore;
        _registeredInCurrentProcess = registeredInCurrentProcess;
    }

    public bool IsOwner => _ownsSemaphore;

    public static ProfileSingleton TryAcquire(ProfileIdentity profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!OwnedProfilesInCurrentProcess.TryAdd(profile.MutexName, 0))
        {
            return new ProfileSingleton(null, profile.MutexName, ownsSemaphore: false, registeredInCurrentProcess: false);
        }

        var semaphore = new Semaphore(initialCount: 1, maximumCount: 1, profile.MutexName);
        try
        {
            var ownsSemaphore = semaphore.WaitOne(TimeSpan.Zero);
            if (!ownsSemaphore)
            {
                OwnedProfilesInCurrentProcess.TryRemove(profile.MutexName, out _);
                semaphore.Dispose();
                return new ProfileSingleton(null, profile.MutexName, ownsSemaphore: false, registeredInCurrentProcess: false);
            }

            return new ProfileSingleton(semaphore, profile.MutexName, ownsSemaphore, registeredInCurrentProcess: ownsSemaphore);
        }
        catch
        {
            OwnedProfilesInCurrentProcess.TryRemove(profile.MutexName, out _);
            semaphore.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_ownsSemaphore)
        {
            _semaphore!.Release();
            _ownsSemaphore = false;
        }

        if (_registeredInCurrentProcess)
        {
            OwnedProfilesInCurrentProcess.TryRemove(_synchronizationName, out _);
        }

        _semaphore?.Dispose();
    }
}
