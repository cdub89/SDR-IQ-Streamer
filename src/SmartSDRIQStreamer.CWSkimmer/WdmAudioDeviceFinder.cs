using System.Runtime.InteropServices;

namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Resolves CW Skimmer audio device indices by enumerating WinMM capture/playback
/// devices at runtime.  All public Find* methods return the 1-based UI display number
/// (WinMM index + 1), which matches what CW Skimmer shows in its Audio tab dropdown.
/// To write to a CW Skimmer INI file subtract 1: INI value = WinMM index.
/// </summary>
public sealed class WdmAudioDeviceFinder : IAudioDeviceFinder
{
    // Last-resort sequential fallback indices (INI 0-based) used only when WinMM
    // runtime enumeration finds no matching device by name.
    private const int FixedSignalBase = 7;
    private const int FixedAudioBase  = 14;

    public int FindSignalDeviceIndex(string nameFragment)
    {
        if (string.IsNullOrWhiteSpace(nameFragment))
            return -1;

        // Primary strategy: runtime WinMM enumeration by endpoint name.
        // This keeps selection aligned with what CW Skimmer sees on this machine.
        var all = EnumerateInputDevices();
        foreach (var (idx, name) in all)
        {
            if (name.StartsWith(nameFragment, StringComparison.OrdinalIgnoreCase))
                return idx;
        }

        // Deterministic fallback for known DAX endpoint patterns.
        if (TryParseChannel("DAX IQ RX ", nameFragment, out int iqChannel))
            return FixedSignalBase + (iqChannel - 1);

        if (TryParseChannel("DAX Audio RX ", nameFragment, out int audioChannel))
            return FixedAudioBase + (audioChannel - 1);

        return -1;
    }

    public int FindAudioDeviceIndex(string nameFragment)
    {
        if (string.IsNullOrWhiteSpace(nameFragment))
            return -1;

        // Audio I/O is resolved from playback devices.
        var all = EnumerateOutputDevices();
        foreach (var (idx, name) in all)
        {
            if (name.StartsWith(nameFragment, StringComparison.OrdinalIgnoreCase))
                return idx;
        }

        // Deterministic fallback for known DAX endpoint patterns.
        if (TryParseChannel("DAX Audio RX ", nameFragment, out int audioChannel))
            return FixedAudioBase + (audioChannel - 1);

        return -1;
    }

    public int FindDaxIqSignalDeviceIndex(int channel)
    {
        // DAX v2 (SmartSDR 4.2.x): "DAX IQ {N} (FlexRadio DAX)" — 24 chars, no WinMM truncation.
        // StartsWith("DAX IQ {N}") cannot match "DAX IQ RX {N}" (R breaks it) — probes are safe.
        int idx = FindSignalDeviceIndex($"DAX IQ {channel}");
        if (idx >= 0) return idx;

        // DAX v1 (SmartSDR 4.1.x): "DAX IQ RX {N} (FlexRadio Systems DAX IQ)" truncated to 31 chars.
        // StartsWith("DAX IQ RX {N}") cannot match "DAX RESERVED IQ RX {N}" — safe exclusion.
        return FindSignalDeviceIndex($"DAX IQ RX {channel}");
    }

    public IReadOnlyList<(int CwSkimmerIndex, string Name)> ListAllSignalDevices()
        => EnumerateInputDevices();

    public IReadOnlyList<(int CwSkimmerIndex, string Name)> ListAllAudioDevices()
        => EnumerateOutputDevices();

    private static IReadOnlyList<(int CwSkimmerIndex, string Name)> EnumerateInputDevices()
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

    private static IReadOnlyList<(int CwSkimmerIndex, string Name)> EnumerateOutputDevices()
    {
        var devices = new List<(int, string)>();

        int count;
        try
        {
            count = waveOutGetNumDevs();
        }
        catch
        {
            return devices;
        }

        for (int winMmIndex = 0; winMmIndex < count; winMmIndex++)
        {
            var caps = new WAVEOUTCAPS();
            int result = waveOutGetDevCaps((uint)winMmIndex, ref caps, (uint)Marshal.SizeOf<WAVEOUTCAPS>());
            if (result != MMSYSERR_NOERROR)
                continue;

            string name = (caps.szPname ?? string.Empty).Trim();
            if (name.Length == 0)
                continue;

            // CW Skimmer WDM list includes "Sound Mapper" at slot 0,
            // so hardware playback devices are WinMM index + 1.
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

    [DllImport("winmm.dll")]
    private static extern int waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveOutGetDevCaps(uint uDeviceID, ref WAVEOUTCAPS pwoc, uint cbwoc);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WAVEOUTCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;

        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
        public uint dwSupport;
    }
}
