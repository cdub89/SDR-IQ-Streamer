namespace SDRIQStreamer.CWSkimmer;

public enum LaunchResult
{
    Success,
    AlreadyRunning,
    ExeNotFound,
    DeviceNotFound,
    ProcessStartFailed
}

/// <summary>
/// Manages the CW Skimmer process lifecycle: write INI, launch, monitor, and stop.
/// Also owns the telnet client for two-way communication (LO freq sync, click→tune).
/// </summary>
public interface ICwSkimmerLauncher
{
    bool IsRunning { get; }
    bool IsChannelRunning(int daxIqChannel);

    /// <summary>Whether the telnet client is currently connected to CW Skimmer.</summary>
    bool TelnetConnected { get; }

    /// <summary>
    /// Human-readable device enumeration report from the most recent launch attempt.
    /// Contains the full WinMM capture device list plus the selected indices.
    /// Empty string before the first launch attempt.
    /// </summary>
    string LastDiagnostics { get; }

    /// <summary>
    /// Returns the WinMM capture device name and CW-Skimmer index for the given DAX-IQ channel,
    /// without launching.  Used to preview the selected endpoints in the UI.
    /// Returns null if no matching device is found.
    /// </summary>
    (string SignalDevice, int SignalIdx, string AudioDevice, int AudioIdx)?
        PreviewDevices(int daxIqChannel);

    /// <summary>Fires on the thread pool when the running state changes.</summary>
    event Action<bool>? RunningStateChanged;

    /// <summary>
    /// Fires when the user clicks on a signal in CW Skimmer.
    /// Argument is the clicked frequency in kHz.
    /// </summary>
    event Action<double>? FrequencyClicked;

    /// <summary>
    /// Writes the INI, optionally waits <see cref="CwSkimmerConfig.LaunchDelaySeconds"/>,
    /// then starts CwSkimmer.exe with <c>ini=&lt;path&gt;</c>.
    /// Connects the telnet client in the background after
    /// <see cref="CwSkimmerConfig.ConnectDelaySeconds"/>.
    /// </summary>
    Task<LaunchResult> LaunchAsync(
        int             daxIqChannel,
        int             sampleRateHz,
        long            centerFreqHz,
        CwSkimmerConfig config);

    /// <summary>
    /// Send <c>SKIMMER/LO_FREQ</c> to update CW Skimmer's centre frequency
    /// when the panadapter moves.  No-op if telnet is not connected.
    /// </summary>
    Task UpdateLoFreqAsync(long freqHz);

    /// <summary>
    /// Send <c>SKIMMER/QSY</c> to update CW Skimmer's operating/VFO frequency
    /// shown in the main window.  No-op if telnet is not connected.
    /// </summary>
    Task UpdateSliceFreqAsync(double freqMHz);

    /// <summary>Kills all CW Skimmer processes and disconnects telnet if running.</summary>
    void Stop();

    /// <summary>Kills a single CW Skimmer process for a DAX-IQ channel if running.</summary>
    void Stop(int daxIqChannel);
}
