using System.Runtime.InteropServices;

namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Enumerates DirectSound capture and output devices in the same order
/// CW Skimmer sees them in its WDM Audio tab dropdown.
///
/// CW Skimmer's "WDM" mode uses DirectSound underneath, so the callback
/// order returned here matches CW Skimmer's WDM slot numbering exactly:
///   slot 1 (UI)  = callback index 0  (null GUID, "Primary Sound ... Driver")
///   slot 2 (UI)  = callback index 1
///   ...
///
/// INI value written to [Audio] WdmSignalDev / WdmAudioDev = callback index
/// (0-based, matching CW Skimmer's UI display number minus 1).
/// </summary>
internal static class DirectSoundProbe
{
    public readonly record struct DirectSoundDevice(int Index, string Description, string Module);

    public static IReadOnlyList<DirectSoundDevice> EnumerateCaptureDevices()
    {
        var list = new List<DirectSoundDevice>();
        DSEnumCallback callback = (IntPtr _, string description, string module, IntPtr _) =>
        {
            list.Add(new DirectSoundDevice(list.Count, description ?? string.Empty, module ?? string.Empty));
            return true;
        };

        try { DirectSoundCaptureEnumerateW(callback, IntPtr.Zero); }
        catch { /* dsound.dll absent or call failed — return whatever we have */ }

        GC.KeepAlive(callback);
        return list;
    }

    public static IReadOnlyList<DirectSoundDevice> EnumerateOutputDevices()
    {
        var list = new List<DirectSoundDevice>();
        DSEnumCallback callback = (IntPtr _, string description, string module, IntPtr _) =>
        {
            list.Add(new DirectSoundDevice(list.Count, description ?? string.Empty, module ?? string.Empty));
            return true;
        };

        try { DirectSoundEnumerateW(callback, IntPtr.Zero); }
        catch { }

        GC.KeepAlive(callback);
        return list;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate bool DSEnumCallback(
        IntPtr lpGuid,
        [MarshalAs(UnmanagedType.LPWStr)] string lpcstrDescription,
        [MarshalAs(UnmanagedType.LPWStr)] string lpcstrModule,
        IntPtr lpContext);

    [DllImport("dsound.dll", CharSet = CharSet.Unicode, EntryPoint = "DirectSoundCaptureEnumerateW")]
    private static extern int DirectSoundCaptureEnumerateW(DSEnumCallback lpDSEnumCallback, IntPtr lpContext);

    [DllImport("dsound.dll", CharSet = CharSet.Unicode, EntryPoint = "DirectSoundEnumerateW")]
    private static extern int DirectSoundEnumerateW(DSEnumCallback lpDSEnumCallback, IntPtr lpContext);
}
