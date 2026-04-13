using System.Diagnostics;
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

    private readonly CwSkimmerIniModelFactory _modelFactory;
    private readonly CwSkimmerIniWriter       _iniWriter;
    private readonly IAudioDeviceFinder       _deviceFinder;
    private readonly ICwSkimmerTelnetClient   _telnet;

    private readonly Dictionary<int, Process> _processesByChannel = new();
    private readonly List<Task> _backgroundTasks = new();
    private readonly object _sync = new();
    private CancellationTokenSource _telnetLifecycleCts = new();
    private int _telnetDisconnectInFlight;
    private bool? _lastEmittedRunningState;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _processesByChannel.Values.Any(p => !p.HasExited);
        }
    }
    public bool TelnetConnected => _telnet.IsConnected;
    public bool IsChannelRunning(int daxIqChannel)
    {
        lock (_sync)
            return _processesByChannel.TryGetValue(daxIqChannel, out var p) && !p.HasExited;
    }

    public event Action<bool>?   RunningStateChanged;
    public event Action<double>? FrequencyClicked;
    public event Action<CwSkimmerSpotInfo>? SpotReceived;
    public event Action<string>? TelnetStatusChanged;

    public string LastDiagnostics { get; private set; } = string.Empty;

    public CwSkimmerLauncher(CwSkimmerIniModelFactory modelFactory,
                             CwSkimmerIniWriter       iniWriter,
                             IAudioDeviceFinder       deviceFinder,
                             ICwSkimmerTelnetClient   telnet)
    {
        _modelFactory = modelFactory;
        _iniWriter    = iniWriter;
        _deviceFinder = deviceFinder;
        _telnet       = telnet;

        _telnet.FrequencyClicked += freq => FrequencyClicked?.Invoke(freq);
        _telnet.SpotReceived += spot => SpotReceived?.Invoke(spot);
        _telnet.StatusChanged += message => TelnetStatusChanged?.Invoke(message);
    }

    public (string SignalDevice, int SignalIdx, string AudioDevice, int AudioIdx)?
        PreviewDevices(int daxIqChannel)
    {
        var all = _deviceFinder.ListAllCaptureDevices();
        string sigName = $"DAX IQ RX {daxIqChannel}";
        string audName = $"DAX Audio RX {daxIqChannel}";

        int sigIdx = _deviceFinder.FindCaptureDeviceIndex(sigName);
        int audIdx = _deviceFinder.FindCaptureDeviceIndex(audName);
        if (sigIdx < 0 && audIdx < 0) return null;

        var sigEntry = all.FirstOrDefault(d => d.Name.StartsWith(sigName, StringComparison.OrdinalIgnoreCase));
        var audEntry = all.FirstOrDefault(d => d.Name.StartsWith(audName, StringComparison.OrdinalIgnoreCase));

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

        var allDevices = _deviceFinder.ListAllCaptureDevices();
        var model      = _modelFactory.Build(daxIqChannel, sampleRateHz, centerFreqHz, config);

        LastDiagnostics = BuildDiagnostics(allDevices, daxIqChannel, model);
        WriteDiagnosticLog(LastDiagnostics);

        if (model.WdmSignalDevIndex < 0) return LaunchResult.DeviceNotFound;

        var iniPath = Path.Combine(IniDir, $"CwSkimmer-ch{daxIqChannel}.ini");
        if (!PrepareManagedIniFromTemplate(iniPath, config, out _))
            return LaunchResult.TemplateIniNotFound;

        _iniWriter.Write(model, iniPath);

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
        process.Exited += (_, _) => OnProcessExited(daxIqChannel);
        lock (_sync)
            _processesByChannel[daxIqChannel] = process;
        EmitRunningStateChangedIfNeeded(true);

        // Connect telnet in the background after CW Skimmer has started up,
        // then immediately sync the VFO frequency.
        var sliceFreqMHz = config.InitialSliceFreqMHz;
        var loFreqHz = config.InitialLoFreqHz;
        if (!_telnet.IsConnected)
            RunBackgroundTask(ConnectTelnetAsync(config, sliceFreqMHz, loFreqHz, _telnetLifecycleCts.Token), "telnet connect");

        return LaunchResult.Success;
    }

    private async Task ConnectTelnetAsync(
        CwSkimmerConfig config,
        double initialSliceFreqMHz,
        long initialLoFreqHz,
        CancellationToken ct)
    {
        if (config.ConnectDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(config.ConnectDelaySeconds), ct);

        if (!IsRunning) return;

        try
        {
            await _telnet.ConnectAsync(
                "127.0.0.1",
                config.TelnetPort,
                config.Callsign,
                config.TelnetPassword,
                ct);

            // Sync initial LO and VFO immediately after connect so CW Skimmer
            // starts on the correct band/frequency context.
            if (initialLoFreqHz > 0)
                await _telnet.SendLoFreqAsync(initialLoFreqHz, ct);

            if (initialSliceFreqMHz > 0)
                await _telnet.SendQsyAsync(initialSliceFreqMHz * 1000.0, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Telnet is best-effort; LO/QSY sync won't work but CW Skimmer still runs.
            LogNonFatal("Background telnet connect failed.", ex);
        }
    }

    public async Task UpdateLoFreqAsync(long freqHz)
    {
        try { await _telnet.SendLoFreqAsync(freqHz); }
        catch (Exception ex) { LogNonFatal("LO frequency sync failed.", ex); }
    }

    public async Task UpdateSliceFreqAsync(double freqMHz)
    {
        try { await _telnet.SendQsyAsync(freqMHz * 1000.0); }
        catch (Exception ex) { LogNonFatal("Slice frequency sync failed.", ex); }
    }

    public void Stop()
    {
        List<Process> procs;
        lock (_sync)
        {
            procs = _processesByChannel.Values.ToList();
            _processesByChannel.Clear();
        }

        foreach (var proc in procs)
        {
            if (proc is { HasExited: false })
            {
                try { proc.Kill(); }
                catch (Exception ex) { LogNonFatal("Failed to kill CW Skimmer process.", ex); }
            }
        }

        CancelPendingTelnetWork();
        BeginTelnetDisconnect();
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
        }

        if (proc is { HasExited: false })
        {
            try { proc.Kill(); }
            catch (Exception ex) { LogNonFatal("Failed to kill CW Skimmer process.", ex); }
        }

        if (!IsRunning)
        {
            CancelPendingTelnetWork();
            BeginTelnetDisconnect();
        }

        EmitRunningStateChangedIfNeeded(IsRunning);
    }

    private void OnProcessExited(int daxIqChannel)
    {
        lock (_sync)
            _processesByChannel.Remove(daxIqChannel);

        if (!IsRunning)
        {
            CancelPendingTelnetWork();
            BeginTelnetDisconnect();
        }
        EmitRunningStateChangedIfNeeded(IsRunning);
    }

    private static string BuildDiagnostics(
        IReadOnlyList<(int CwSkimmerIndex, string Name)> devices,
        int daxIqChannel,
        CwSkimmerIniModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== CW Skimmer Device Diagnostic  (DAX ch {daxIqChannel}) ===");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("--- WinMM WaveIn capture devices (CwSkimmerIndex: name) ---");
        if (devices.Count == 0)
            sb.AppendLine("  (none found — SmartSDR/DAX may not be running)");
        else
            foreach (var (idx, name) in devices)
                sb.AppendLine($"  [{idx:D2}] {name}");
        sb.AppendLine();
        sb.AppendLine("--- Selected for INI ---");
        sb.AppendLine($"  WdmSignalDev = {model.WdmSignalDevIndex}  " +
                      $"(searching for \"DAX IQ RX {daxIqChannel}\")");
        sb.AppendLine($"  WdmAudioDev  = {model.WdmAudioDevIndex}  " +
                      $"(searching for \"DAX Audio RX {daxIqChannel}\")");
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

    private void BeginTelnetDisconnect()
    {
        if (Interlocked.CompareExchange(ref _telnetDisconnectInFlight, 1, 0) != 0)
            return;

        RunBackgroundTask(DisconnectTelnetAsync(), "telnet disconnect");
    }

    private void CancelPendingTelnetWork()
    {
        CancellationTokenSource toDispose;
        lock (_sync)
        {
            toDispose = _telnetLifecycleCts;
            _telnetLifecycleCts = new CancellationTokenSource();
        }

        try { toDispose.Cancel(); }
        catch (ObjectDisposedException) { }
        finally { toDispose.Dispose(); }
    }

    private async Task DisconnectTelnetAsync()
    {
        try { await _telnet.DisconnectAsync(); }
        catch (Exception ex) { LogNonFatal("Background telnet disconnect failed.", ex); }
        finally { Interlocked.Exchange(ref _telnetDisconnectInFlight, 0); }
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
        CancelPendingTelnetWork();
        Stop();
        lock (_sync)
            _telnetLifecycleCts.Dispose();
    }
}
