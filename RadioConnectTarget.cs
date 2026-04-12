using SDRIQStreamer.FlexRadio;

namespace SDRIQStreamer.App;

/// <summary>
/// A concrete radio + station selection target shown in the connect list.
/// </summary>
public sealed record RadioConnectTarget(DiscoveredRadio Radio, string Station)
{
    public const string UnknownStation = "(unknown)";

    public string DisplayLabel => $"{Radio.DisplayLabel}  |  Station: {Station}";
}
