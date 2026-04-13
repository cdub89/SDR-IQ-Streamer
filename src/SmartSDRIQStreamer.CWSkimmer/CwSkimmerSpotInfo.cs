namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Parsed DX spot emitted by CW Skimmer telnet feed.
/// Frequency is reported in kHz by CW Skimmer.
/// </summary>
public sealed record CwSkimmerSpotInfo(
    double FrequencyKhz,
    string Callsign,
    string Spotter,
    int? SignalDb,
    int? SpeedWpm,
    string Comment);
