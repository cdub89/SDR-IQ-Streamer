namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// All values required to write a CW Skimmer INI file for one DAX-IQ channel.
/// Constructed at launch time from live radio state and device enumeration.
/// </summary>
public sealed record CwSkimmerIniModel(
    /// <summary>
    /// CW Skimmer WDM signal device index for channel N, derived from manual
    /// IQ1 calibration and channel offset.
    /// Written to [Audio] WdmSignalDev. -1 when calibration is unavailable.
    /// </summary>
    int WdmSignalDevIndex,

    /// <summary>
    /// CW Skimmer WDM audio device index for channel N, derived from manual
    /// Audio RX1 calibration and channel offset.
    /// Written to [Audio] WdmAudioDev. -1 when calibration is unavailable.
    /// </summary>
    int WdmAudioDevIndex,

    /// <summary>
    /// MME signal device index copied from the operator-calibrated template INI.
    /// </summary>
    int MmeSignalDevIndex,

    /// <summary>
    /// MME audio device index copied from the operator-calibrated template INI.
    /// </summary>
    int MmeAudioDevIndex,

    /// <summary>
    /// Baseline WDM signal index from manual IQ1 calibration.
    /// </summary>
    int CalibrationBaseSignalIndex,

    /// <summary>
    /// Baseline WDM audio index from manual Audio RX1 calibration.
    /// </summary>
    int CalibrationBaseAudioIndex,

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
