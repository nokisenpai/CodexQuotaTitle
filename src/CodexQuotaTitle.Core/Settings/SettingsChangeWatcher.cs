namespace CodexQuotaTitle.Settings;

public interface ISettingsChangeWatcher : IDisposable
{
    Task WaitForChangeAsync(CancellationToken cancellationToken);
}

public interface ISettingsChangeWatcherFactory
{
    ISettingsChangeWatcher Create(CodexQuotaTitleSettingsPaths paths);
}

public sealed class SettingsChangeWatcherFactory : ISettingsChangeWatcherFactory
{
    public ISettingsChangeWatcher Create(CodexQuotaTitleSettingsPaths paths) => new SettingsChangeWatcher(paths);
}

public sealed class SettingsChangeWatcher : ISettingsChangeWatcher
{
    private readonly FileSystemWatcher _watcher;
    private readonly SemaphoreSlim _changes = new(0, 1);

    public SettingsChangeWatcher(CodexQuotaTitleSettingsPaths paths)
    {
        Directory.CreateDirectory(paths.DirectoryPath);
        _watcher = new FileSystemWatcher(paths.DirectoryPath, Path.GetFileName(paths.SettingsPath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnRenamed;
    }

    public Task WaitForChangeAsync(CancellationToken cancellationToken) => _changes.WaitAsync(cancellationToken);

    public void Dispose()
    {
        _watcher.Dispose();
        _changes.Dispose();
    }

    private void OnChanged(object? sender, FileSystemEventArgs args) => Signal();

    private void OnRenamed(object? sender, RenamedEventArgs args) => Signal();

    private void Signal()
    {
        try
        {
            _changes.Release();
        }
        catch (SemaphoreFullException)
        {
            // A refresh is already pending; one is sufficient.
        }
    }
}
