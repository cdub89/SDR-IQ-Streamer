namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Builds a <see cref="CwSkimmerIniModel"/> from live radio state by resolving
/// the Windows audio device indices for the requested DAX-IQ channel.
/// </summary>
public sealed class CwSkimmerIniModelFactory
{
    private readonly IAudioDeviceFinder _deviceFinder;

    public CwSkimmerIniModelFactory(IAudioDeviceFinder deviceFinder)
        => _deviceFinder = deviceFinder;

    /// <summary>
    /// Resolves device indices and assembles the INI model.
    /// </summary>
    /// <param name="daxIqChannel">1-based DAX-IQ channel (1–4).</param>
    /// <param name="sampleRateHz">Stream sample rate from FlexLib (e.g. 48000).</param>
    /// <param name="centerFreqHz">Panadapter center frequency in Hz.</param>
    /// <param name="config">Persisted user settings.</param>
    public CwSkimmerIniModel Build(
        int              daxIqChannel,
        int              sampleRateHz,
        long             centerFreqHz,
        CwSkimmerConfig  config)
    {
        string signalDevName = $"DAX IQ RX {daxIqChannel}";
        string audioDevName  = $"DAX Audio RX {daxIqChannel}";

        int wdmSignal = _deviceFinder.FindCaptureDeviceIndex(signalDevName);
        int wdmAudio  = _deviceFinder.FindCaptureDeviceIndex(audioDevName);

        return new CwSkimmerIniModel(
            WdmSignalDevIndex: wdmSignal,
            WdmAudioDevIndex:  wdmAudio,
            SampleRateHz:      sampleRateHz,
            CenterFreqHz:      centerFreqHz,
            Config:            config);
    }
}
