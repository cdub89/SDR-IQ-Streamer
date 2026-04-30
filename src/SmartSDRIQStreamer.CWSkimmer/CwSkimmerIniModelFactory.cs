namespace SDRIQStreamer.CWSkimmer;

using System.Globalization;

/// <summary>
/// Builds a <see cref="CwSkimmerIniModel"/> for a specific DAX-IQ channel.
///
/// Strategy: MME-only auto-derivation.
///   CW Skimmer's WDM mode uses an opaque kernel-streaming enumeration whose
///   slot ordering is non-monotonic and not reproducible from outside CW Skimmer
///   (DirectSound and WinMM both produce a different ordering on the same machine).
///   Without that ordering we cannot compute a correct WdmSignalDev for any
///   channel beyond the user's manually-calibrated baseline channel.
///
///   MME (WinMM) is fully enumerable by name, so per-channel MmeSignalDev is
///   resolved by looking up "DAX IQ {N}" (DAX v2) or "DAX IQ RX {N}" (DAX v1)
///   in the live device list. MmeAudioDev (the user's local speakers/headphones)
///   is copied verbatim from the master INI.
///
///   Generated channel INIs always set UseWdm=false. WDM fields are propagated
///   from the master INI for diagnostic/sanity but are inert at runtime.
/// </summary>
public sealed class CwSkimmerIniModelFactory
{
    private readonly IAudioDeviceFinder _deviceFinder;

    public CwSkimmerIniModelFactory(IAudioDeviceFinder deviceFinder)
    {
        _deviceFinder = deviceFinder;
    }

    public CwSkimmerIniModel Build(
        int              daxIqChannel,
        int              sampleRateHz,
        long             centerFreqHz,
        CwSkimmerConfig  config)
    {
        if (!TryReadCalibration(config.SkimmerIniPath,
                out var wdmIQ1, out var wdmAudio,
                out var mmeIQ1, out var mmeAudio))
        {
            return new CwSkimmerIniModel(
                WdmSignalDevIndex:          -1,
                WdmAudioDevIndex:           -1,
                MmeSignalDevIndex:          -1,
                MmeAudioDevIndex:           0,
                UseWdm:                     false,
                CalibrationBaseSignalIndex: -1,
                CalibrationBaseAudioIndex:  -1,
                SampleRateHz:               sampleRateHz,
                CenterFreqHz:               centerFreqHz,
                Config:                     config);
        }

        // MME signal: per-channel WinMM name lookup.
        // FindDaxIqSignalDeviceIndex returns the 1-based UI display number;
        // CW Skimmer INI stores 0-based, so subtract 1.
        var uiMmeN = _deviceFinder.FindDaxIqSignalDeviceIndex(daxIqChannel);
        var mmeSignal = uiMmeN >= 0
            ? uiMmeN - 1                              // UI 1-based → INI 0-based
            : mmeIQ1 + (daxIqChannel - 1);            // sequential fallback

        return new CwSkimmerIniModel(
            WdmSignalDevIndex:          wdmIQ1,        // copied from master, inert (UseWdm=false)
            WdmAudioDevIndex:           wdmAudio,      // copied from master, inert (UseWdm=false)
            MmeSignalDevIndex:          mmeSignal,
            MmeAudioDevIndex:           mmeAudio,
            UseWdm:                     false,         // always MME for generated channel INIs
            CalibrationBaseSignalIndex: wdmIQ1,
            CalibrationBaseAudioIndex:  wdmAudio,
            SampleRateHz:               sampleRateHz,
            CenterFreqHz:               centerFreqHz,
            Config:                     config);
    }

    private static bool TryReadCalibration(
        string templateIniPath,
        out int wdmIQ1,
        out int wdmAudio,
        out int mmeIQ1,
        out int mmeAudio)
    {
        wdmIQ1   = -1;
        wdmAudio = -1;
        mmeIQ1   = 0;
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

            var idx   = line.IndexOf('=');
            var key   = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                continue;

            if (key.Equals("WdmSignalDev", StringComparison.OrdinalIgnoreCase))
                wdmIQ1 = parsed;
            else if (key.Equals("WdmAudioDev", StringComparison.OrdinalIgnoreCase))
                wdmAudio = parsed;
            else if (key.Equals("MmeSignalDev", StringComparison.OrdinalIgnoreCase))
                mmeIQ1 = parsed;
            else if (key.Equals("MmeAudioDev", StringComparison.OrdinalIgnoreCase))
                mmeAudio = parsed;
        }

        // Master INI must be calibrated to at least know the user's audio output.
        // We accept the calibration if WDM fields are present (legacy users) OR
        // if MmeAudioDev is set (post-pivot users who calibrated in MME mode).
        return wdmIQ1 >= 0 && wdmAudio >= 0;
    }
}
