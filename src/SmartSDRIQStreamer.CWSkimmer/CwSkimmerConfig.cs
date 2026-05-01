namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Persisted user settings for the CW Skimmer adapter.
/// </summary>
public sealed record CwSkimmerConfig
{
    /// <summary>Full path to CwSkimmer.exe.</summary>
    public string ExePath   { get; init; } = @"C:\Program Files (x86)\Afreet\CwSkimmer\CwSkimmer.exe";
    /// <summary>
    /// Path to the user-maintained CW Skimmer INI file (typically from a manual CW Skimmer run).
    /// Used as the source template when rebuilding streamer-managed INI files.
    /// </summary>
    public string SkimmerIniPath { get; init; } = string.Empty;

    /// <summary>Seconds to wait after process start before connecting the telnet client.</summary>
    public int ConnectDelaySeconds { get; init; } = 5;

    /// <summary>Seconds to wait before launching the process.</summary>
    public int LaunchDelaySeconds  { get; init; } = 3;

    // ── Session identity ──────────────────────────────────────────────────────

    public string Callsign  { get; init; } = string.Empty;

    // ── Telnet server settings (used by Phase 3 client) ───────────────────────

    /// <summary>
    /// Telnet port for this CW Skimmer instance.
    /// Reference: 7300 + (daxChannel × 10) — IQ1=7310, IQ2=7320, IQ3=7330, IQ4=7340.
    /// </summary>
    public int    TelnetPort              { get; init; } = 7310;
    public bool   TelnetPasswordRequired  { get; init; } = false;
    public string TelnetPassword          { get; init; } = "";
    public bool   TelnetClusterEnabled    { get; init; } = true;

    /// <summary>
    /// Slice VFO frequency in MHz at launch time.
    /// Sent as SKIMMER/QSY after telnet connects so the CW Skimmer main window
    /// shows the correct operating frequency immediately.
    /// </summary>
    public double InitialSliceFreqMHz { get; init; } = 0;

    /// <summary>
    /// Panadapter centre frequency in Hz at launch time.
    /// Sent as SKIMMER/LO_FREQ after telnet connects so CW Skimmer starts on the
    /// correct band/LO context immediately.
    /// </summary>
    public long InitialLoFreqHz { get; init; } = 0;

    /// <summary>
    /// Operator-supplied 1-based MME signal device index for this channel
    /// (as shown in CW Skimmer's Audio tab MME dropdown). When set, overrides
    /// auto-derivation. Null = auto-derive from WinMM enumeration.
    /// </summary>
    public int? OperatorMmeSignalDevIndex { get; init; }

    /// <summary>
    /// Operator-supplied 1-based WDM signal device index for this channel
    /// (as shown in CW Skimmer's Audio tab WDM dropdown). When set, this
    /// channel's INI is written with UseWdm=1 and the supplied index.
    /// Null = use MME mode (current default).
    /// </summary>
    public int? OperatorWdmSignalDevIndex { get; init; }
}
