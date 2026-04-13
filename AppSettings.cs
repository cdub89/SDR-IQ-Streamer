namespace SDRIQStreamer.App;

/// <summary>
/// Persisted application settings (serialized to JSON).
/// </summary>
public sealed class AppSettings
{
    // ── CW Skimmer paths ──────────────────────────────────────────────────────

    public string CwSkimmerExePath   { get; set; } = string.Empty;
    public string CwSkimmerIniPath   { get; set; } = string.Empty;

    // ── Timing ────────────────────────────────────────────────────────────────

    public int ConnectDelaySeconds { get; set; } = 5;
    public int LaunchDelaySeconds  { get; set; } = 3;

    // ── Session identity ──────────────────────────────────────────────────────

    public string Callsign { get; set; } = string.Empty;

    // ── Main window placement ─────────────────────────────────────────────────

    public double? MainWindowX { get; set; }
    public double? MainWindowY { get; set; }
    public double? MainWindowWidth { get; set; }
    public double? MainWindowHeight { get; set; }
}
