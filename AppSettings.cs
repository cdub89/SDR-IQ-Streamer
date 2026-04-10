namespace SDRIQStreamer.App;

/// <summary>
/// Persisted application settings (serialized to JSON).
/// </summary>
public sealed class AppSettings
{
    // ── CW Skimmer paths ──────────────────────────────────────────────────────

    public string CwSkimmerExePath   { get; set; } = string.Empty;

    // ── Timing ────────────────────────────────────────────────────────────────

    public int ConnectDelaySeconds { get; set; } = 5;
    public int LaunchDelaySeconds  { get; set; } = 3;

    // ── Station info (written to [Recorder]) ──────────────────────────────────

    public string Callsign   { get; set; } = string.Empty;
    public string Operator   { get; set; } = string.Empty;
    public string Location   { get; set; } = string.Empty;
    public string GridSquare { get; set; } = string.Empty;
    public string IqWavDir   { get; set; } = string.Empty;

    // ── CW ────────────────────────────────────────────────────────────────────

    public int CwPitch { get; set; } = 600;

    // ── Main window placement ─────────────────────────────────────────────────

    public double? MainWindowX { get; set; }
    public double? MainWindowY { get; set; }
    public double? MainWindowWidth { get; set; }
    public double? MainWindowHeight { get; set; }
}
