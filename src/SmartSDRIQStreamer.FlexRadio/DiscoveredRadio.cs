using System.Net;

namespace SDRIQStreamer.FlexRadio;

/// <summary>
/// Immutable snapshot of a radio discovered on the local network.
/// Contains only the information needed by the UI — no FlexLib types exposed.
/// </summary>
public sealed record DiscoveredRadio(
    string Serial,
    string Model,
    string Nickname,
    string Callsign,
    IPAddress IP,
    string Status)
{
    /// <summary>
    /// A human-readable label suitable for display in a radio picker list.
    /// </summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Nickname)
            ? $"{Model}  [{Callsign}]  {IP}"
            : $"{Model}  {Nickname}  [{Callsign}]  {IP}";
}
