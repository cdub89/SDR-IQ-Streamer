namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Manages a live telnet session with a running CW Skimmer process.
/// </summary>
public interface ICwSkimmerTelnetClient : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>
    /// Emits key telnet lifecycle/status messages suitable for UI status display.
    /// </summary>
    event Action<string>? StatusChanged;

    /// <summary>
    /// Fires when the user clicks on a signal in CW Skimmer.
    /// Argument is the clicked frequency in kHz.
    /// </summary>
    event Action<double>? FrequencyClicked;

    /// <summary>
    /// Fires when CW Skimmer emits a DX spot line on telnet.
    /// </summary>
    event Action<CwSkimmerSpotInfo>? SpotReceived;

    /// <summary>
    /// Connect to the CW Skimmer telnet server and log in.
    /// Starts the background receive loop.
    /// </summary>
    Task ConnectAsync(string host, int port, string callsign, string password,
                      CancellationToken ct = default);

    /// <summary>
    /// Send <c>SKIMMER/LO_FREQ &lt;freqHz&gt;</c> to update CW Skimmer's
    /// LO/centre frequency when the panadapter moves.
    /// No-op if not connected.
    /// </summary>
    Task SendLoFreqAsync(long freqHz, CancellationToken ct = default);

    /// <summary>
    /// Send <c>SKIMMER/QSY &lt;freqKhz&gt;</c> to update CW Skimmer's
    /// operating/VFO frequency shown in the main window.
    /// No-op if not connected.
    /// </summary>
    Task SendQsyAsync(double freqKhz, CancellationToken ct = default);

    /// <summary>Disconnect gracefully and release resources.</summary>
    Task DisconnectAsync();
}
