namespace SDRIQStreamer.FlexRadio;

/// <summary>
/// Spot payload to publish to the radio.
/// </summary>
public sealed record RadioSpotInfo(
    string Callsign,
    double RxFrequencyMHz,
    string Source,
    string? SpotterCallsign = null,
    string? Comment = null,
    string? Mode = null,
    string? Color = null,
    string? BackgroundColor = null,
    int LifetimeSeconds = 120);
