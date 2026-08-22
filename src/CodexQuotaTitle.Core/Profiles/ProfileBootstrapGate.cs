namespace CodexQuotaTitle.Profiles;

public sealed class ProfileBootstrapGate : IDisposable
{
    private readonly Mutex _mutex;
    private bool _isOwner;

    private ProfileBootstrapGate(Mutex mutex)
    {
        _mutex = mutex;
        _isOwner = true;
    }

    public static ProfileBootstrapGate Acquire(ProfileIdentity profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var mutex = new Mutex(false, profile.BootstrapMutexName);
        try
        {
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new InvalidOperationException("Unable to acquire the profile bootstrap gate.");
            }

            return new ProfileBootstrapGate(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_isOwner)
        {
            _mutex.ReleaseMutex();
            _isOwner = false;
        }

        _mutex.Dispose();
    }
}
