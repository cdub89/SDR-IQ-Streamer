namespace SDRIQStreamer.FlexRadio;

/// <summary>
/// Abstraction over local radio discovery.
/// The rest of the app depends on this interface; FlexLib types do not leak through.
/// </summary>
public interface IRadioDiscovery
{
    /// <summary>Snapshot of radios currently visible on the local network.</summary>
    IReadOnlyList<DiscoveredRadio> DiscoveredRadios { get; }

    /// <summary>Start listening for radio discovery broadcasts (UDP 4992).</summary>
    void Start();

    /// <summary>Stop listening and release resources.</summary>
    void Stop();

    /// <summary>Fires on the thread pool when a new radio appears on the network.</summary>
    event Action<DiscoveredRadio> RadioAdded;

    /// <summary>Fires on the thread pool when a previously-seen radio is no longer heard.</summary>
    event Action<DiscoveredRadio> RadioRemoved;
}
