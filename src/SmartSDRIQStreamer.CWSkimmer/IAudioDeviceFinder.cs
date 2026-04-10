namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Finds Windows audio capture device indices by name fragment.
/// Abstraction over NAudio/WinMM so the INI model factory is unit-testable
/// without requiring real audio devices.
/// </summary>
public interface IAudioDeviceFinder
{
    /// <summary>
    /// Returns the CW Skimmer-compatible WDM device index for the first WaveIn
    /// capture device whose product name starts with <paramref name="nameFragment"/>
    /// (case-insensitive).
    ///
    /// StartsWith is used because WinMM device names include a vendor suffix, e.g.
    /// "DAX IQ RX 1 (FlexRadio Systems DAX IQ)", while the search term is the short
    /// name "DAX IQ RX 1".  StartsWith correctly excludes "DAX RESERVED IQ RX N"
    /// and "DAX RESERVED AUDIO RX N" devices that FlexRadio also registers.
    ///
    /// The returned value is the WinMM zero-based device index + 1, because CW Skimmer
    /// places the Sound Mapper at slot 0 in its WDM device list, shifting all real
    /// devices up by one.
    ///
    /// Returns -1 if no matching device is found.
    /// </summary>
    int FindCaptureDeviceIndex(string nameFragment);

    /// <summary>
    /// Returns all enumerated capture devices as (CwSkimmerIndex, ProductName) pairs,
    /// ordered by index.  CwSkimmerIndex = WinMM zero-based index + 1.
    /// Used for diagnostics and logging.
    /// </summary>
    IReadOnlyList<(int CwSkimmerIndex, string Name)> ListAllCaptureDevices();
}
