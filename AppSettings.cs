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

    // ── Per-channel CW Skimmer device indices (operator-supplied) ────────────
    // 1-based UI numbers as shown in CW Skimmer's Audio tab dropdowns.
    // Null = auto-derive at launch (current behavior).

    public int? MmeDeviceIndexCh1 { get; set; }
    public int? MmeDeviceIndexCh2 { get; set; }
    public int? MmeDeviceIndexCh3 { get; set; }
    public int? MmeDeviceIndexCh4 { get; set; }

    public int? WdmDeviceIndexCh1 { get; set; }
    public int? WdmDeviceIndexCh2 { get; set; }
    public int? WdmDeviceIndexCh3 { get; set; }
    public int? WdmDeviceIndexCh4 { get; set; }

    /// <summary>
    /// Set to true once the operator has been shown the Reset/Setup wizard at
    /// least once after configuring both CW Skimmer paths. Prevents the wizard
    /// from re-opening on subsequent launches.
    /// </summary>
    public bool HasShownSkimmerSetupWizard { get; set; }

    /// <summary>
    /// CW Skimmer Soundcard Driver mode applied to all generated channel INIs.
    /// "MME" (default, recommended) or "WDM" (experimental, requires per-channel
    /// indices on PCs where WDM ordering differs from auto-derivation).
    /// </summary>
    public string SkimmerSoundcardDriverMode { get; set; } = "MME";
}
