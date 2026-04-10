using System.Text;

namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Writes a CW Skimmer Afreet-format INI file from a <see cref="CwSkimmerIniModel"/>.
/// Only the sections owned by this app are written; CW Skimmer manages the rest
/// ([Windows], [BandMap], [Validation], etc.) itself on first run.
/// </summary>
public sealed class CwSkimmerIniWriter
{
    /// <summary>
    /// Generates the INI content and writes it to <paramref name="path"/>,
    /// creating or overwriting the file.
    /// </summary>
    public void Write(CwSkimmerIniModel model, string path)
    {
        var cfg = model.Config;
        var sb  = new StringBuilder();

        // ── [Windows] ─────────────────────────────────────────────────────────
        // Ensure CW Skimmer waterfall uses color mode by default.
        sb.AppendLine("[Windows]");
        sb.AppendLine("Colors=1");
        sb.AppendLine();

        // ── [Audio] ───────────────────────────────────────────────────────────
        sb.AppendLine("[Audio]");
        sb.AppendLine($"WdmSignalDev={model.WdmSignalDevIndex}");
        sb.AppendLine($"WdmAudioDev={model.WdmAudioDevIndex}");
        sb.AppendLine("MmeSignalDev=0");
        sb.AppendLine("MmeAudioDev=0");
        sb.AppendLine("UseWdm=1");
        sb.AppendLine("ShiftQ=0");
        sb.AppendLine("SwapIQ=0");
        sb.AppendLine();

        // ── [Radio] ───────────────────────────────────────────────────────────
        sb.AppendLine("[Radio]");
        sb.AppendLine("SdrType=2");
        sb.AppendLine($"Pitch={cfg.CwPitch}");
        sb.AppendLine("EstimateIQBalance=1");
        sb.AppendLine("CorrectIQBalance=1");
        sb.AppendLine();

        // ── [sdrSR] ───────────────────────────────────────────────────────────
        // SdrType=2 in [Radio] selects SoftRock, which reads from [sdrSR].
        sb.AppendLine("[sdrSR]");
        sb.AppendLine($"signalrate={model.SampleRateHz}");
        sb.AppendLine($"centerfreq={model.CenterFreqHz}");
        sb.AppendLine("centeroffset=0");
        sb.AppendLine("rigno=0");
        sb.AppendLine();

        // ── [Recorder] ────────────────────────────────────────────────────────
        sb.AppendLine("[Recorder]");
        if (!string.IsNullOrEmpty(cfg.IqWavDir))
            sb.AppendLine($"WavDir={cfg.IqWavDir}");
        sb.AppendLine("Visible=0");
        sb.AppendLine("Loop=0");
        sb.AppendLine($"WavCall={cfg.Callsign}");
        sb.AppendLine($"WavOper={cfg.Operator}");
        sb.AppendLine($"WavQTH={cfg.Location}");
        sb.AppendLine($"WavGrid={cfg.GridSquare}");
        sb.AppendLine();

        // ── [Telnet] ──────────────────────────────────────────────────────────
        sb.AppendLine("[Telnet]");
        sb.AppendLine($"Port={cfg.TelnetPort}");
        sb.AppendLine($"PasswordRequired={(cfg.TelnetPasswordRequired ? 1 : 0)}");
        sb.AppendLine($"Password={cfg.TelnetPassword}");
        sb.AppendLine("CqOnly=0");
        sb.AppendLine("AllowAnn=1");
        sb.AppendLine("AnnUserOnly=0");
        sb.AppendLine("AnnUser=");
        sb.AppendLine("TelnetSrvEnabled=1");
        sb.AppendLine("UdpSourceName=CW Skimmer");
        sb.AppendLine("UdpAddress=127.0.0.1");
        sb.AppendLine("UdpPort=13064");
        sb.AppendLine("UdpEnabled=0");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, sb.ToString());
    }
}
