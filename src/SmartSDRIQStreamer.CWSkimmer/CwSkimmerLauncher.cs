using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Writes the CW Skimmer INI, launches CwSkimmer.exe, monitors the process,
/// and manages the telnet connection for two-way sync.
/// </summary>
public sealed class CwSkimmerLauncher : ICwSkimmerLauncher, IDisposable
{
    private static readonly string IniDir =
        RuntimePathResolver.ResolveCwSkimmerIniDir();
    private static readonly TimeSpan GracefulStopWait = TimeSpan.FromSeconds(2);

    private readonly CwSkimmerIniModelFactory _modelFactory;
    private readonly CwSkimmerIniWriter       _iniWriter;
    private readonly IAudioDeviceFinder       _deviceFinder;
    private readonly Func<ICwSkimmerTelnetClient> _telnetFactory;

    private readonly Dictionary<int, Process> _processesByChannel = new();
    private readonly Dictionary<int, ICwSkimmerTelnetClient> _telnetByChannel = new();
    private readonly Dictionary<int, CancellationTokenSource> _telnetLifecycleCtsByChannel = new();
    private readonly Dictionary<int, int> _telnetPortByChannel = new();
    private readonly Dictionary<int, string> _managedIniPathByChannel = new();
    private readonly Dictionary<int, DateTime> _processStartUtcByChannel = new();
    private readonly Dictionary<int, string> _requestedStopReasonByChannel = new();
    private readonly List<Task> _backgroundTasks = new();
    private readonly object _sync = new();
    private readonly HashSet<int> _telnetDisconnectInFlightChannels = [];
    private bool? _lastEmittedRunningState;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _processesByChannel.Values.Any(p => !p.HasExited);
        }
    }
    public bool TelnetConnected
    {
        get
        {
            lock (_sync)
                return _telnetByChannel.Values.Any(t => t.IsConnected);
        }
    }
    public bool IsChannelRunning(int daxIqChannel)
    {
        lock (_sync)
            return _processesByChannel.TryGetValue(daxIqChannel, out var p) && !p.HasExited;
    }

    public event Action<bool>?   RunningStateChanged;
    public event Action<int, double>? FrequencyClicked;
    public event Action<int, CwSkimmerSpotInfo>? SpotReceived;
    public event Action<string>? TelnetStatusChanged;

    public string LastDiagnostics { get; private set; } = string.Empty;

    public CwSkimmerLauncher(CwSkimmerIniModelFactory modelFactory,
                             CwSkimmerIniWriter       iniWriter,
                             IAudioDeviceFinder       deviceFinder,
                             Func<ICwSkimmerTelnetClient> telnetFactory)
    {
        _modelFactory = modelFactory;
        _iniWriter    = iniWriter;
        _deviceFinder = deviceFinder;
        _telnetFactory = telnetFactory;
    }

    public (string SignalDevice, int SignalIdx, string AudioDevice, int AudioIdx)?
        PreviewDevices(int daxIqChannel)
    {
        var signalDevices = _deviceFinder.ListAllSignalDevices();
        var audioDevices = _deviceFinder.ListAllAudioDevices();
        string sigName = $"DAX IQ RX {daxIqChannel}";
        string audName = $"DAX Audio RX {daxIqChannel}";

        int sigIdx = _deviceFinder.FindSignalDeviceIndex(sigName);
        int audIdx = _deviceFinder.FindAudioDeviceIndex(audName);
        if (sigIdx < 0 && audIdx < 0) return null;

        var sigEntry = signalDevices.FirstOrDefault(d => d.Name.StartsWith(sigName, StringComparison.OrdinalIgnoreCase));
        var audEntry = audioDevices.FirstOrDefault(d => d.Name.StartsWith(audName, StringComparison.OrdinalIgnoreCase));

        string signalLabel = sigEntry == default ? sigName : sigEntry.Name;
        string audioLabel  = audEntry == default ? audName : audEntry.Name;

        return (signalLabel, sigIdx, audioLabel, audIdx);
    }

    public async Task<LaunchResult> LaunchAsync(
        int             daxIqChannel,
        int             sampleRateHz,
        long            centerFreqHz,
        CwSkimmerConfig config)
    {
        if (IsChannelRunning(daxIqChannel)) return LaunchResult.AlreadyRunning;

        string exePath = config.ExePath;
        if (!File.Exists(exePath)) return LaunchResult.ExeNotFound;

        var model      = _modelFactory.Build(daxIqChannel, sampleRateHz, centerFreqHz, config);
        var iniPath = Path.Combine(IniDir, $"CwSkimmer-ch{daxIqChannel}.ini");
        var channelIniExists = File.Exists(iniPath);

        LastDiagnostics = BuildDiagnostics(daxIqChannel, model, config.SkimmerIniPath, channelIniExists);
        WriteDiagnosticLog(LastDiagnostics);

        if (model.WdmSignalDevIndex < 0 || model.WdmAudioDevIndex < 0)
            return LaunchResult.DeviceNotFound;

        if (!PrepareManagedIniFromTemplate(iniPath, config, out _))
            return LaunchResult.TemplateIniNotFound;

        // Audio section is seeded from calibrated baseline only on first channel INI
        // creation. After that, operator edits in CW Skimmer are preserved.
        if (!channelIniExists)
            _iniWriter.Write(model, iniPath);
        lock (_sync)
        {
            _managedIniPathByChannel[daxIqChannel] = iniPath;
        }
        var resolvedTelnetPort = TryReadTelnetPortFromIni(iniPath) ?? config.TelnetPort;
        lock (_sync)
            _telnetPortByChannel[daxIqChannel] = resolvedTelnetPort;

        if (config.LaunchDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(config.LaunchDelaySeconds));

        var psi = new ProcessStartInfo
        {
            FileName        = exePath,
            Arguments       = $"ini=\"{iniPath}\"",
            UseShellExecute = false,
        };

        Process? process;
        try { process = Process.Start(psi); }
        catch
        {
            return LaunchResult.ProcessStartFailed;
        }

        if (process is null) return LaunchResult.ProcessStartFailed;

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => OnProcessExited(daxIqChannel, process);
        lock (_sync)
        {
            _processesByChannel[daxIqChannel] = process;
            _processStartUtcByChannel[daxIqChannel] = DateTime.UtcNow;
            _requestedStopReasonByChannel.Remove(daxIqChannel);
        }
        EmitRunningStateChangedIfNeeded(true);

        // Connect telnet in the background after CW Skimmer has started up,
        // then immediately sync the VFO frequency.
        var sliceFreqMHz = config.InitialSliceFreqMHz;
        var loFreqHz = config.InitialLoFreqHz;
        if (!IsTelnetConnected(daxIqChannel))
        {
            var telnetCts = GetOrCreateTelnetLifecycleCts(daxIqChannel);
            RunBackgroundTask(
                ConnectTelnetAsync(daxIqChannel, resolvedTelnetPort, config, sliceFreqMHz, loFreqHz, telnetCts.Token),
                $"telnet connect ch {daxIqChannel}");
        }

        return LaunchResult.Success;
    }

    private async Task ConnectTelnetAsync(
        int daxIqChannel,
        int telnetPort,
        CwSkimmerConfig config,
        double initialSliceFreqMHz,
        long initialLoFreqHz,
        CancellationToken ct)
    {
        if (config.ConnectDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(config.ConnectDelaySeconds), ct);

        if (!IsChannelRunning(daxIqChannel))
        {
            EmitLauncherStatus(daxIqChannel, "Skipping telnet connect because CW Skimmer process is not running.");
            return;
        }

        const int maxConnectAttempts = 3;
        var retryDelays = new[]
        {
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(800),
            TimeSpan.FromMilliseconds(1600)
        };

        for (var attempt = 1; attempt <= maxConnectAttempts; attempt++)
        {
            try
            {
                var settleDelay = retryDelays[Math.Min(attempt - 1, retryDelays.Length - 1)];
                if (settleDelay > TimeSpan.Zero)
                    await Task.Delay(settleDelay, ct);

                var telnet = GetOrCreateTelnetClient(daxIqChannel);
                await telnet.ConnectAsync(
                    "127.0.0.1",
                    telnetPort,
                    config.Callsign,
                    config.TelnetPassword,
                    ct);

                if (config.TelnetClusterEnabled)
                {
                    // Sync initial LO and VFO immediately after connect so CW Skimmer
                    // starts on the correct band/frequency context.
                    if (initialLoFreqHz > 0)
                        await telnet.SendLoFreqAsync(initialLoFreqHz, ct);

                    if (initialSliceFreqMHz > 0)
                        await telnet.SendQsyAsync(initialSliceFreqMHz * 1000.0, ct);
                }

                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (attempt < maxConnectAttempts && IsRetriableStartupConnectFailure(ex))
            {
                LogNonFatal($"Telnet connect retry {attempt}/{maxConnectAttempts - 1} after startup race.", ex);
            }
            catch (Exception ex)
            {
                // Telnet is best-effort; LO/QSY sync won't work but CW Skimmer still runs.
                LogNonFatal("Background telnet connect failed.", ex);
                return;
            }
        }
    }

    public async Task UpdateLoFreqAsync(int daxIqChannel, long freqHz)
    {
        if (!TryGetTelnetClient(daxIqChannel, out var telnet))
            return;

        try { await telnet.SendLoFreqAsync(freqHz); }
        catch (Exception ex) { LogNonFatal("LO frequency sync failed.", ex); }
    }

    public async Task UpdateSliceFreqAsync(int daxIqChannel, double freqMHz)
    {
        if (!TryGetTelnetClient(daxIqChannel, out var telnet))
            return;

        try { await telnet.SendQsyAsync(freqMHz * 1000.0); }
        catch (Exception ex) { LogNonFatal("Slice frequency sync failed.", ex); }
    }

    public void Stop()
    {
        List<int> channels;
        List<Process> procs;
        lock (_sync)
        {
            channels = _processesByChannel.Keys.ToList();
            procs = _processesByChannel.Values.ToList();
            _processesByChannel.Clear();
            foreach (var channel in channels)
                _requestedStopReasonByChannel[channel] = "stop requested by application (all channels).";
        }

        foreach (var proc in procs)
        {
            TryStopProcessGracefully(proc);
        }

        foreach (var channel in channels)
        {
            CancelPendingTelnetWork(channel);
            BeginTelnetDisconnect(channel);
        }

        List<int> telnetOnlyChannels;
        lock (_sync)
            telnetOnlyChannels = _telnetByChannel.Keys.Except(channels).ToList();

        foreach (var channel in telnetOnlyChannels)
        {
            CancelPendingTelnetWork(channel);
            BeginTelnetDisconnect(channel);
        }
        EmitRunningStateChangedIfNeeded(IsRunning);
    }

    public void Stop(int daxIqChannel)
    {
        Process? proc = null;
        lock (_sync)
        {
            if (_processesByChannel.TryGetValue(daxIqChannel, out var p))
            {
                proc = p;
                _processesByChannel.Remove(daxIqChannel);
            }
            _requestedStopReasonByChannel[daxIqChannel] = "stop requested by application (single channel).";
        }

        if (proc is not null)
            TryStopProcessGracefully(proc);

        CancelPendingTelnetWork(daxIqChannel);
        BeginTelnetDisconnect(daxIqChannel);

        EmitRunningStateChangedIfNeeded(IsRunning);
    }

    private void OnProcessExited(int daxIqChannel, Process process)
    {
        DateTime? startUtc = null;
        string requestedStopReason = string.Empty;
        int? exitCode = null;
        try
        {
            if (process.HasExited)
                exitCode = process.ExitCode;
        }
        catch
        {
            // Ignore metadata fetch failures for exited process.
        }

        lock (_sync)
        {
            _processesByChannel.Remove(daxIqChannel);
            if (_processStartUtcByChannel.TryGetValue(daxIqChannel, out var started))
            {
                startUtc = started;
                _processStartUtcByChannel.Remove(daxIqChannel);
            }
            if (_requestedStopReasonByChannel.TryGetValue(daxIqChannel, out var reason))
            {
                requestedStopReason = reason;
                _requestedStopReasonByChannel.Remove(daxIqChannel);
            }
        }

        var uptime = startUtc.HasValue
            ? DateTime.UtcNow - startUtc.Value
            : (TimeSpan?)null;
        var reasonText = string.IsNullOrWhiteSpace(requestedStopReason)
            ? "process exited without app stop request."
            : requestedStopReason;
        var exitCodeText = exitCode.HasValue ? exitCode.Value.ToString(CultureInfo.InvariantCulture) : "unknown";
        var uptimeText = uptime.HasValue ? $"{uptime.Value.TotalSeconds:F1}s" : "unknown";
        EmitLauncherStatus(
            daxIqChannel,
            $"CW Skimmer process exited (exit_code={exitCodeText}, uptime={uptimeText}, reason={reasonText})");

        CancelPendingTelnetWork(daxIqChannel);
        BeginTelnetDisconnect(daxIqChannel);
        EmitRunningStateChangedIfNeeded(IsRunning);
    }

    private static string BuildDiagnostics(
        int daxIqChannel,
        CwSkimmerIniModel model,
        string templateIniPath,
        bool channelIniExists)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== CW Skimmer Device Diagnostic  (DAX ch {daxIqChannel}) ===");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("--- Calibration source ---");
        sb.AppendLine($"  Template INI = {templateIniPath}");
        sb.AppendLine($"  Channel INI exists = {channelIniExists}");
        sb.AppendLine();
        sb.AppendLine("--- Selected for INI ---");
        sb.AppendLine($"  Baseline WdmSignalDev = {model.CalibrationBaseSignalIndex}");
        sb.AppendLine($"  Baseline WdmAudioDev  = {model.CalibrationBaseAudioIndex}");
        sb.AppendLine($"  WdmSignalDev          = {model.WdmSignalDevIndex}");
        sb.AppendLine($"  WdmAudioDev           = {model.WdmAudioDevIndex}");
        sb.AppendLine($"  MmeSignalDev          = {model.MmeSignalDevIndex}");
        sb.AppendLine($"  MmeAudioDev           = {model.MmeAudioDevIndex}");
        sb.AppendLine($"  SignalRate   = {model.SampleRateHz}");
        sb.AppendLine($"  CenterFreq   = {model.CenterFreqHz}");
        return sb.ToString();
    }

    private static void WriteDiagnosticLog(string content)
    {
        try
        {
            Directory.CreateDirectory(IniDir);
            File.WriteAllText(Path.Combine(IniDir, "device-diagnostic.txt"), content);
        }
        catch (Exception ex) { LogNonFatal("Failed to write diagnostic log.", ex); }
    }

    private bool PrepareManagedIniFromTemplate(string targetIniPath, CwSkimmerConfig config, out string templateIniPath)
    {
        // Preserve per-channel window geometry and other CW-managed sections by
        // reusing an existing managed INI when present.
        if (File.Exists(targetIniPath))
        {
            templateIniPath = targetIniPath;
            return true;
        }

        templateIniPath = ResolveTemplateIniPath(config);
        if (string.IsNullOrWhiteSpace(templateIniPath))
            return false;

        try
        {
            var dir = Path.GetDirectoryName(targetIniPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.Copy(templateIniPath, targetIniPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LogNonFatal("Failed to seed managed INI from template.", ex);
            return false;
        }
    }

    private string ResolveTemplateIniPath(CwSkimmerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.SkimmerIniPath) && File.Exists(config.SkimmerIniPath))
            return config.SkimmerIniPath;

        return string.Empty;
    }

    private static int? TryReadTelnetPortFromIni(string iniPath)
    {
        if (!File.Exists(iniPath))
            return null;

        var inTelnetSection = false;
        foreach (var raw in File.ReadLines(iniPath))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inTelnetSection = string.Equals(line, "[Telnet]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inTelnetSection)
                continue;

            if (!line.StartsWith("Port=", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line["Port=".Length..].Trim();
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port > 0)
                return port;
        }

        return null;
    }

    private bool IsTelnetConnected(int daxIqChannel)
    {
        lock (_sync)
            return _telnetByChannel.TryGetValue(daxIqChannel, out var telnet) && telnet.IsConnected;
    }

    private bool TryGetTelnetClient(int daxIqChannel, out ICwSkimmerTelnetClient telnet)
    {
        lock (_sync)
            return _telnetByChannel.TryGetValue(daxIqChannel, out telnet!);
    }

    private ICwSkimmerTelnetClient GetOrCreateTelnetClient(int daxIqChannel)
    {
        lock (_sync)
        {
            if (_telnetByChannel.TryGetValue(daxIqChannel, out var existing))
                return existing;

            var telnet = _telnetFactory();
            telnet.FrequencyClicked += freq => FrequencyClicked?.Invoke(daxIqChannel, freq);
            telnet.SpotReceived += spot => SpotReceived?.Invoke(daxIqChannel, spot);
            telnet.StatusChanged += message =>
            {
                if (message.Contains("Telnet connection closed by CW Skimmer.", StringComparison.OrdinalIgnoreCase))
                {
                    message = $"{message} (process_running={IsChannelRunning(daxIqChannel)})";
                }
                var portText = TryGetKnownTelnetPortText(daxIqChannel);
                TelnetStatusChanged?.Invoke($"ch {daxIqChannel}{portText}: {message}");
            };
            _telnetByChannel[daxIqChannel] = telnet;
            return telnet;
        }
    }

    private string TryGetKnownTelnetPortText(int daxIqChannel)
    {
        lock (_sync)
        {
            if (_telnetPortByChannel.TryGetValue(daxIqChannel, out var port) && port > 0)
                return $" ({port})";
        }

        return string.Empty;
    }

    private CancellationTokenSource GetOrCreateTelnetLifecycleCts(int daxIqChannel)
    {
        lock (_sync)
        {
            if (_telnetLifecycleCtsByChannel.TryGetValue(daxIqChannel, out var existing))
                return existing;

            var created = new CancellationTokenSource();
            _telnetLifecycleCtsByChannel[daxIqChannel] = created;
            return created;
        }
    }

    private void BeginTelnetDisconnect(int daxIqChannel)
    {
        lock (_sync)
        {
            if (_telnetDisconnectInFlightChannels.Contains(daxIqChannel))
                return;

            _telnetDisconnectInFlightChannels.Add(daxIqChannel);
        }

        RunBackgroundTask(DisconnectTelnetAsync(daxIqChannel), $"telnet disconnect ch {daxIqChannel}");
    }

    private void CancelPendingTelnetWork(int daxIqChannel)
    {
        CancellationTokenSource? toDispose = null;
        lock (_sync)
        {
            if (_telnetLifecycleCtsByChannel.TryGetValue(daxIqChannel, out var current))
            {
                toDispose = current;
                _telnetLifecycleCtsByChannel[daxIqChannel] = new CancellationTokenSource();
            }
        }

        if (toDispose is null)
            return;

        try { toDispose.Cancel(); }
        catch (ObjectDisposedException) { }
        finally { toDispose.Dispose(); }
    }

    private async Task DisconnectTelnetAsync(int daxIqChannel)
    {
        ICwSkimmerTelnetClient? telnet = null;
        CancellationTokenSource? lifecycleCts = null;
        lock (_sync)
        {
            if (_telnetByChannel.TryGetValue(daxIqChannel, out var existing))
            {
                telnet = existing;
                _telnetByChannel.Remove(daxIqChannel);
            }

            if (_telnetLifecycleCtsByChannel.TryGetValue(daxIqChannel, out var cts))
            {
                lifecycleCts = cts;
                _telnetLifecycleCtsByChannel.Remove(daxIqChannel);
            }

            _telnetPortByChannel.Remove(daxIqChannel);
            _managedIniPathByChannel.Remove(daxIqChannel);
        }

        try
        {
            if (telnet is not null)
            {
                try { await telnet.DisconnectAsync(); }
                finally { await telnet.DisposeAsync(); }
            }
        }
        catch (Exception ex)
        {
            LogNonFatal($"Background telnet disconnect failed (ch {daxIqChannel}).", ex);
        }
        finally
        {
            if (lifecycleCts is not null)
            {
                try { lifecycleCts.Cancel(); } catch { }
                lifecycleCts.Dispose();
            }

            lock (_sync)
                _telnetDisconnectInFlightChannels.Remove(daxIqChannel);
        }
    }

    private void RunBackgroundTask(Task task, string operation)
    {
        lock (_sync)
            _backgroundTasks.Add(task);

        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                LogNonFatal($"Background operation failed: {operation}.", t.Exception?.GetBaseException());

            lock (_sync)
                _backgroundTasks.Remove(task);
        }, TaskScheduler.Default);
    }

    private static void LogNonFatal(string message, Exception? ex)
    {
        if (ex is null)
            Debug.WriteLine($"[CwSkimmerLauncher] {message}");
        else
            Debug.WriteLine($"[CwSkimmerLauncher] {message} {ex.Message}");
    }

    private static bool IsRetriableStartupConnectFailure(Exception ex)
    {
        if (ex is SocketException socketEx && socketEx.SocketErrorCode == SocketError.ConnectionRefused)
            return true;

        return false;
    }

    private void EmitLauncherStatus(int daxIqChannel, string message)
    {
        var portText = TryGetKnownTelnetPortText(daxIqChannel);
        TelnetStatusChanged?.Invoke($"ch {daxIqChannel}{portText}: {message}");
    }

    private static void TryStopProcessGracefully(Process proc)
    {
        if (proc.HasExited)
            return;

        try
        {
            var closeRequested = proc.CloseMainWindow();
            if (closeRequested && proc.WaitForExit(GracefulStopWait))
                return;
        }
        catch (Exception ex)
        {
            LogNonFatal("Graceful CW Skimmer stop failed, falling back to kill.", ex);
        }

        if (proc.HasExited)
            return;

        try
        {
            proc.Kill();
        }
        catch (Exception ex)
        {
            LogNonFatal("Failed to kill CW Skimmer process.", ex);
        }
    }

    private void EmitRunningStateChangedIfNeeded(bool running)
    {
        lock (_sync)
        {
            if (_lastEmittedRunningState.HasValue && _lastEmittedRunningState.Value == running)
                return;

            _lastEmittedRunningState = running;
        }

        RunningStateChanged?.Invoke(running);
    }

    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            foreach (var cts in _telnetLifecycleCtsByChannel.Values)
                cts.Dispose();
            _telnetLifecycleCtsByChannel.Clear();
        }
    }
}
