namespace SDRIQStreamer.App;

/// <summary>
/// In-memory settings session shared across UI components.
/// Load once at startup, save once at shutdown.
/// </summary>
public sealed class AppSettingsSession
{
    private readonly AppSettingsStore _store;

    public AppSettings Settings { get; }

    public AppSettingsSession(AppSettingsStore store)
    {
        _store = store;
        Settings = _store.Load();
    }

    public void Save() => _store.Save(Settings);
}
