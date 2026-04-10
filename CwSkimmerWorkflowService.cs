using System;
using System.Linq;
using System.Threading.Tasks;
using SDRIQStreamer.CWSkimmer;
using SDRIQStreamer.FlexRadio;

namespace SDRIQStreamer.App;

/// <summary>
/// Encapsulates CW Skimmer launch/stop workflow and status formatting.
/// </summary>
public sealed class CwSkimmerWorkflowService
{
    private readonly IRadioConnection _connection;
    private readonly ICwSkimmerLauncher _launcher;
    private readonly AppSettings _settings;

    public CwSkimmerWorkflowService(
        IRadioConnection connection,
        ICwSkimmerLauncher launcher,
        AppSettings settings)
    {
        _connection = connection;
        _launcher = launcher;
        _settings = settings;
    }

    public bool CanLaunch(DaxIQStreamInfo? stream, string exePath) =>
        stream is not null &&
        !string.IsNullOrWhiteSpace(exePath) &&
        !_launcher.IsChannelRunning(stream.DAXIQChannel);

    public bool CanStop(DaxIQStreamInfo? stream) =>
        stream is not null && _launcher.IsChannelRunning(stream.DAXIQChannel);

    public async Task LaunchForChannelAsync(DaxIQStreamInfo? stream, string exePath, Action<string> addStatus)
    {
        if (stream is null) return;

        if (stream.CenterFreqMHz <= 0)
        {
            addStatus($"ch {stream.DAXIQChannel}: center frequency not yet available.");
            return;
        }

        var pan = _connection.Panadapters.FirstOrDefault(p => p.DAXIQChannel == stream.DAXIQChannel);
        var slice = pan is not null
            ? _connection.Slices.FirstOrDefault(s => s.PanadapterStreamId == pan.StreamId)
            : _connection.Slices.FirstOrDefault();

        var config = new CwSkimmerConfig
        {
            ExePath = exePath,
            ConnectDelaySeconds = _settings.ConnectDelaySeconds,
            LaunchDelaySeconds = _settings.LaunchDelaySeconds,
            Callsign = _settings.Callsign,
            Operator = _settings.Operator,
            Location = _settings.Location,
            GridSquare = _settings.GridSquare,
            IqWavDir = _settings.IqWavDir,
            CwPitch = _settings.CwPitch,
            TelnetPort = 7300 + (stream.DAXIQChannel * 10),
            InitialSliceFreqMHz = slice?.FreqMHz ?? 0,
        };

        var centerFreqHz = (long)(stream.CenterFreqMHz * 1_000_000);
        addStatus($"Launching CW Skimmer on ch {stream.DAXIQChannel} ({stream.SampleRate / 1000} kHz).");

        var result = await _launcher.LaunchAsync(
            stream.DAXIQChannel, stream.SampleRate, centerFreqHz, config);

        var status = result switch
        {
            LaunchResult.Success => FormatLaunchSuccess(),
            LaunchResult.AlreadyRunning => "Already running.",
            LaunchResult.ExeNotFound => "CW Skimmer exe not found — check the path.",
            LaunchResult.DeviceNotFound => FormatDeviceNotFound(stream.DAXIQChannel),
            LaunchResult.ProcessStartFailed => "Failed to start CW Skimmer process.",
            _ => "Launch failed."
        };
        addStatus(status);
    }

    public void StopForChannel(DaxIQStreamInfo? stream, Action<string> addStatus)
    {
        if (stream is null) return;
        _launcher.Stop(stream.DAXIQChannel);
        addStatus($"Stopped CW Skimmer on channel {stream.DAXIQChannel}.");
    }

    private string FormatLaunchSuccess()
    {
        var diag = _launcher.LastDiagnostics;
        var loLine = diag.Split('\n').FirstOrDefault(l => l.Contains("CenterFreq"))?.Trim() ?? "";
        var signalLine = diag.Split('\n').FirstOrDefault(l => l.Contains("WdmSignalDev"))?.Trim() ?? "";
        var audioLine = diag.Split('\n').FirstOrDefault(l => l.Contains("WdmAudioDev"))?.Trim() ?? "";
        return $"CW Skimmer running  |  {loLine}  |  {signalLine}  |  {audioLine}";
    }

    private string FormatDeviceNotFound(int channel)
    {
        var diag = _launcher.LastDiagnostics;
        if (string.IsNullOrEmpty(diag))
            return $"DAX IQ RX {channel} audio device not found.";

        var lines = diag.Split('\n')
            .SkipWhile(l => !l.Contains("WinMM WaveIn"))
            .Take(12)
            .ToArray();
        return $"DAX IQ RX {channel} not found in WinMM list:\n{string.Join("\n", lines)}\n" +
               "(Full log: %TEMP%\\SDRIQStreamer\\device-diagnostic.txt)";
    }
}
