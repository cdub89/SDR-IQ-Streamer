namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// All values required to write a CW Skimmer INI file for one DAX-IQ channel.
/// Constructed at launch time from live radio state and device enumeration.
/// </summary>
public sealed record CwSkimmerIniModel(
    /// <summary>
    /// Zero-based WaveIn capture device index for "DAX IQ RX N".
    /// Written to [Audio] WdmSignalDev.  -1 if the device was not found.
    /// </summary>
    int WdmSignalDevIndex,

    /// <summary>
    /// Zero-based WaveIn capture device index for "DAX Audio RX N".
    /// Written to [Audio] WdmAudioDev.  -1 if the device was not found.
    /// </summary>
    int WdmAudioDevIndex,

    /// <summary>DAX-IQ stream sample rate in Hz (e.g. 48000). Written to [sdrSDRIQ] SignalRate.</summary>
    int SampleRateHz,

    /// <summary>
    /// Panadapter center frequency in Hz (e.g. 14048441).
    /// Written to [sdrSDRIQ] CenterFreq.  Must be refreshed when the pan moves.
    /// </summary>
    long CenterFreqHz,

    /// <summary>User / app settings to embed in the INI.</summary>
    CwSkimmerConfig Config
);
