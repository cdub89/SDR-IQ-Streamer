namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Persisted user settings for the CW Skimmer adapter.
/// </summary>
public sealed record CwSkimmerConfig
{
    /// <summary>Full path to CwSkimmer.exe.</summary>
    public string ExePath   { get; init; } = @"C:\Program Files (x86)\Afreet\CwSkimmer\CwSkimmer.exe";

    /// <summary>Seconds to wait after process start before connecting the telnet client.</summary>
    public int ConnectDelaySeconds { get; init; } = 5;

    /// <summary>Seconds to wait before launching the process.</summary>
    public int LaunchDelaySeconds  { get; init; } = 3;

    // ── Station info (written into [Recorder]) ────────────────────────────────

    public string Callsign  { get; init; } = string.Empty;
    public string Operator  { get; init; } = string.Empty;
    public string Location  { get; init; } = string.Empty;
    public string GridSquare { get; init; } = string.Empty;

    /// <summary>Directory for IQ .wav recordings ([Recorder] WavDir).</summary>
    public string IqWavDir  { get; init; } = string.Empty;

    // ── CW settings ───────────────────────────────────────────────────────────

    /// <summary>CW pitch in Hz written to [Radio] Pitch.</summary>
    public int CwPitch { get; init; } = 600;

    // ── Telnet server settings (used by Phase 3 client) ───────────────────────

    /// <summary>
    /// Telnet port for this CW Skimmer instance.
    /// Reference: 7300 + (daxChannel × 10) — IQ1=7310, IQ2=7320, IQ3=7330, IQ4=7340.
    /// </summary>
    public int    TelnetPort              { get; init; } = 7310;
    public bool   TelnetPasswordRequired  { get; init; } = false;
    public string TelnetPassword          { get; init; } = "";

    /// <summary>
    /// Slice VFO frequency in MHz at launch time.
    /// Sent as SKIMMER/QSY after telnet connects so the CW Skimmer main window
    /// shows the correct operating frequency immediately.
    /// </summary>
    public double InitialSliceFreqMHz { get; init; } = 0;
}
