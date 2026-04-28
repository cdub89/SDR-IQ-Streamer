namespace SDRIQStreamer.CWSkimmer;

using System.Globalization;

/// <summary>
/// Builds a <see cref="CwSkimmerIniModel"/> using the operator-calibrated
/// manual CW Skimmer INI (IQ1/Audio1) as the machine-local baseline.
/// </summary>
public sealed class CwSkimmerIniModelFactory
{
    /// <summary>
    /// Resolves channel device indices from manual IQ1/Audio1 calibration.
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
        if (!TryReadCalibration(config.SkimmerIniPath, out var baselineSignal, out var baselineAudio, out var mmeSignal, out var mmeAudio))
        {
            return new CwSkimmerIniModel(
                WdmSignalDevIndex: -1,
                WdmAudioDevIndex: -1,
                MmeSignalDevIndex: 0,
                MmeAudioDevIndex: 0,
                CalibrationBaseSignalIndex: -1,
                CalibrationBaseAudioIndex: -1,
                SampleRateHz: sampleRateHz,
                CenterFreqHz: centerFreqHz,
                Config: config);
        }

        var channelOffset = Math.Max(0, daxIqChannel - 1);
        var wdmSignal = baselineSignal + channelOffset;
        var wdmAudio = baselineAudio + channelOffset;

        return new CwSkimmerIniModel(
            WdmSignalDevIndex: wdmSignal,
            WdmAudioDevIndex:  wdmAudio,
            MmeSignalDevIndex: mmeSignal,
            MmeAudioDevIndex: mmeAudio,
            CalibrationBaseSignalIndex: baselineSignal,
            CalibrationBaseAudioIndex: baselineAudio,
            SampleRateHz:      sampleRateHz,
            CenterFreqHz:      centerFreqHz,
            Config:            config);
    }

    private static bool TryReadCalibration(
        string templateIniPath,
        out int baselineSignal,
        out int baselineAudio,
        out int mmeSignal,
        out int mmeAudio)
    {
        baselineSignal = -1;
        baselineAudio = -1;
        mmeSignal = 0;
        mmeAudio = 0;

        if (string.IsNullOrWhiteSpace(templateIniPath) || !File.Exists(templateIniPath))
            return false;

        bool inAudio = false;
        foreach (var raw in File.ReadLines(templateIniPath))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inAudio = string.Equals(line, "[Audio]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inAudio || !line.Contains('='))
                continue;

            var idx = line.IndexOf('=');
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                continue;

            if (key.Equals("WdmSignalDev", StringComparison.OrdinalIgnoreCase))
                baselineSignal = parsed;
            else if (key.Equals("WdmAudioDev", StringComparison.OrdinalIgnoreCase))
                baselineAudio = parsed;
            else if (key.Equals("MmeSignalDev", StringComparison.OrdinalIgnoreCase))
                mmeSignal = parsed;
            else if (key.Equals("MmeAudioDev", StringComparison.OrdinalIgnoreCase))
                mmeAudio = parsed;
        }

        return baselineSignal >= 0 && baselineAudio >= 0;
    }
}
