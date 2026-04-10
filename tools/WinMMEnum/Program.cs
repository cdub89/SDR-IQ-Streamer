// 32-bit WinMM capture device enumerator.
// Outputs one device name per line, index 0 first.
// Must stay x86 so its waveInGetDevCaps results match 32-bit CW Skimmer exactly.
using System;
using System.Runtime.InteropServices;

static class Program
{
    [DllImport("winmm.dll")] static extern int waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    static extern int waveInGetDevCaps(int id, ref WAVEINCAPS caps, int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct WAVEINCAPS
    {
        public ushort wMid, wPid;
        public uint   vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint   dwFormats;
        public ushort wChannels, wReserved1;
    }

    static void Main()
    {
        int count = waveInGetNumDevs();
        for (int i = 0; i < count; i++)
        {
            var caps = new WAVEINCAPS();
            if (waveInGetDevCaps(i, ref caps, Marshal.SizeOf(caps)) == 0)
                Console.WriteLine(caps.szPname ?? string.Empty);
        }
    }
}
