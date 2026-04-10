using System.Runtime.InteropServices;

namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Resolves CW Skimmer WDM indices by enumerating WinMM capture devices at runtime.
/// This avoids machine-specific hardcoded offsets and automatically adapts to radios
/// that expose different numbers/order of DAX endpoints.
/// </summary>
public sealed class WdmAudioDeviceFinder : IAudioDeviceFinder
{
    // Required CW Skimmer mapping (INI is 0-based, UI display is +1):
    //   DAX IQ RX 1 -> 7 (UI "08"), DAX IQ RX 2 -> 8, ...
    //   DAX Audio RX 1 -> 14 (UI "15"), DAX Audio RX 2 -> 15, ...
    private const int FixedSignalBase = 7;
    private const int FixedAudioBase  = 14;

    public int FindCaptureDeviceIndex(string nameFragment)
    {
        if (string.IsNullOrWhiteSpace(nameFragment))
            return -1;

        // Primary strategy: preserve explicit mapping logic for CW Skimmer.
        if (TryParseChannel("DAX IQ RX ", nameFragment, out int iqChannel))
        {
            return FixedSignalBase + (iqChannel - 1);
        }

        if (TryParseChannel("DAX Audio RX ", nameFragment, out int audioChannel))
        {
            return FixedAudioBase + (audioChannel - 1);
        }

        // Fallback strategy: runtime WinMM enumeration by endpoint name.
        foreach (var (idx, name) in EnumerateCaptureDevices())
        {
            if (name.StartsWith(nameFragment, StringComparison.OrdinalIgnoreCase))
                return idx;
        }

        return -1;
    }

    public IReadOnlyList<(int CwSkimmerIndex, string Name)> ListAllCaptureDevices()
        => EnumerateCaptureDevices();

    private static IReadOnlyList<(int CwSkimmerIndex, string Name)> EnumerateCaptureDevices()
    {
        var devices = new List<(int, string)>();

        int count;
        try
        {
            count = waveInGetNumDevs();
        }
        catch
        {
            return devices;
        }

        for (int winMmIndex = 0; winMmIndex < count; winMmIndex++)
        {
            var caps = new WAVEINCAPS();
            int result = waveInGetDevCaps((uint)winMmIndex, ref caps, (uint)Marshal.SizeOf<WAVEINCAPS>());
            if (result != MMSYSERR_NOERROR)
                continue;

            string name = (caps.szPname ?? string.Empty).Trim();
            if (name.Length == 0)
                continue;

            // CW Skimmer WDM list includes "Sound Mapper" at slot 0,
            // so hardware capture devices are WinMM index + 1.
            int cwSkimmerIndex = winMmIndex + 1;
            devices.Add((cwSkimmerIndex, name));
        }

        return devices;
    }

    private static bool TryParseChannel(string prefix, string name, out int channel)
    {
        channel = 0;
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!int.TryParse(name.AsSpan(prefix.Length).Trim(), out channel))
            return false;

        return channel > 0;
    }

    private const int MMSYSERR_NOERROR = 0;

    [DllImport("winmm.dll")]
    private static extern int waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveInGetDevCaps(uint uDeviceID, ref WAVEINCAPS pwic, uint cbwic);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WAVEINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;

        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
    }
}
