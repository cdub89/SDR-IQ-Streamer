using System;

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
    public int TelnetPortBase { get; set; } = 7300;
    public bool TelnetClusterEnabled { get; set; } = true;

    // ── Spot forwarding ───────────────────────────────────────────────────────

    public bool SpotForwardingEnabled { get; set; } = true;
    public int SpotLifetimeSeconds { get; set; } = 300;
    public string SpotColor { get; set; } = "#FF00FFFF";
    public string SpotBackgroundColor { get; set; } = "#00000000";

    // ── Update checks ──────────────────────────────────────────────────────────

    public bool UpdateAutoCheckEnabled { get; set; } = true;
    public int UpdateCheckIntervalMinutes { get; set; } = 30;
    public DateTime? UpdateLastCheckedUtc { get; set; }

    // ── Main window placement ─────────────────────────────────────────────────

    public double? MainWindowX { get; set; }
    public double? MainWindowY { get; set; }
    public double? MainWindowWidth { get; set; }
    public double? MainWindowHeight { get; set; }
}
