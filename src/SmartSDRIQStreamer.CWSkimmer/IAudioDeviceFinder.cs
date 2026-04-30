namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Finds Windows audio device indices by name fragment.
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
    int FindSignalDeviceIndex(string nameFragment);

    /// <summary>
    /// Returns the CW Skimmer-compatible WDM device index for the first WaveOut
    /// playback device whose product name starts with <paramref name="nameFragment"/>
    /// (case-insensitive). Returns -1 if no matching device is found.
    /// </summary>
    int FindAudioDeviceIndex(string nameFragment);

    /// <summary>
    /// Returns all enumerated signal/input devices as (CwSkimmerIndex, ProductName) pairs,
    /// ordered by index.  CwSkimmerIndex = WinMM zero-based index + 1.
    /// Used for diagnostics and logging.
    /// </summary>
    IReadOnlyList<(int CwSkimmerIndex, string Name)> ListAllSignalDevices();

    /// <summary>
    /// Returns all enumerated audio/output devices as (CwSkimmerIndex, ProductName) pairs,
    /// ordered by index.  CwSkimmerIndex = WinMM zero-based index + 1.
    /// Used for diagnostics and logging.
    /// </summary>
    IReadOnlyList<(int CwSkimmerIndex, string Name)> ListAllAudioDevices();

    /// <summary>
    /// Finds the DAX IQ capture device for the given 1-based channel number,
    /// probing DAX v2 naming first ("DAX IQ {N}", SmartSDR 4.2.x) then DAX v1
    /// ("DAX IQ RX {N}", SmartSDR 4.1.x).
    ///
    /// Returns the 1-based UI display number (WinMM index + 1), matching what
    /// CW Skimmer shows in its Audio tab dropdown.  To write to a CW Skimmer INI
    /// file, subtract 1: INI value = WinMM index = UI display number - 1.
    ///
    /// Returns -1 if no matching device is found for either naming convention.
    /// </summary>
    int FindDaxIqSignalDeviceIndex(int channel);
}
