using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDRIQStreamer.CWSkimmer;
using SDRIQStreamer.FlexRadio;

namespace SDRIQStreamer.App;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IRadioDiscovery   _discovery;
    private readonly IRadioConnection  _connection;
    private readonly ICwSkimmerLauncher _launcher;
    private readonly AppSettingsSession _settingsSession;
    private readonly AppSettings _settings;
    private readonly FooterStatusBuffer _footerStatusBuffer;
    private readonly CwSkimmerWorkflowService _cwSkimmerWorkflow;

    // ── Discovered radios ─────────────────────────────────────────────────────

    public ObservableCollection<DiscoveredRadio> Radios { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor("ConnectCommand")]
    private DiscoveredRadio? _selectedRadio;

    [ObservableProperty]
    private string _statusText = "Discovering…";

    // ── Connection state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor("ConnectCommand")]
    [NotifyCanExecuteChangedFor("DisconnectCommand")]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    // ── Post-connect details ──────────────────────────────────────────────────

    /// <summary>Hierarchical view: client → panadapter(s) → slice(s).</summary>
    public ObservableCollection<ClientGroup> ClientGroups { get; } = new();

    public ObservableCollection<DaxIQStreamInfo> DaxIQStreams { get; } = new();

    [ObservableProperty]
    private int _avgDAXKbps;
    private int _radioAvgDaxKbps;

    [ObservableProperty]
    private string _daxStreamingSummary = "DAX Streaming : 0.0 Mbps (0 kbps)";

    [ObservableProperty]
    private string _streamRequestStatus = string.Empty;

    /// <summary>Station name of our own connected client — used to gate the Stream button.</summary>
    public string OwnClientStation { get; private set; } = string.Empty;

    // ── CW Skimmer ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCwSkimmerForChannelCommand))]
    private string _cwSkimmerExePath = string.Empty;

    [ObservableProperty]
    private bool _isCwSkimmerRunning;

    public ObservableCollection<string> FooterStatusLines { get; } = new();
    private readonly Dictionary<int, string> _lastCwDevicePreviewByChannel = new();

    [ObservableProperty]
    private string _footerStatusText = string.Empty;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindowViewModel(IRadioDiscovery discovery, IRadioConnection connection,
                               ICwSkimmerLauncher launcher, AppSettingsSession settingsSession)
    {
        _discovery     = discovery;
        _connection    = connection;
        _launcher      = launcher;
        _settingsSession = settingsSession;
        _settings = _settingsSession.Settings;
        _footerStatusBuffer = new FooterStatusBuffer(FooterStatusLines);
        _cwSkimmerWorkflow = new CwSkimmerWorkflowService(_connection, _launcher, _settings);

        _discovery.RadioAdded   += OnRadioAdded;
        _discovery.RadioRemoved += OnRadioRemoved;

        _connection.ConnectionStateChanged += OnConnectionStateChanged;
        _connection.PanadapterAdded        += p => UIPost(() => AddPan(p));
        _connection.PanadapterRemoved      += p => UIPost(() => RemovePan(p));
        _connection.PanadapterUpdated      += p => UIPost(() => UpdatePan(p));
        _connection.SliceAdded             += s => UIPost(() => AddSlice(s));
        _connection.SliceRemoved           += s => UIPost(() => RemoveSlice(s));
        _connection.SliceUpdated           += s => UIPost(() => UpdateSlice(s));
        _connection.DaxIQStreamAdded       += d => UIPost(() => OnDaxIQStreamAdded(d));
        _connection.DaxIQStreamRemoved     += d => UIPost(() => OnDaxIQStreamRemoved(d));
        _connection.DaxIQStreamUpdated     += d => UIPost(() => OnDaxIQStreamUpdated(d));
        _connection.AvgDAXKbpsChanged      += kbps => UIPost(() =>
        {
            _radioAvgDaxKbps = kbps;
            UpdateDisplayedDaxKbps();
        });

        // Re-evaluate Launch command whenever the stream list changes
        DaxIQStreams.CollectionChanged += (_, _) =>
            UIPost(() =>
            {
                LaunchCwSkimmerForChannelCommand.NotifyCanExecuteChanged();
                StopCwSkimmerForChannelCommand.NotifyCanExecuteChanged();
            });

        // Track CW Skimmer process state
        _launcher.RunningStateChanged += running =>
            UIPost(() =>
            {
                IsCwSkimmerRunning = running;
                LaunchCwSkimmerForChannelCommand.NotifyCanExecuteChanged();
                StopCwSkimmerForChannelCommand.NotifyCanExecuteChanged();
                RefreshDaxStreamPanBindings();
                RefreshAllPanStreamSummaries();
                if (!running)
                    AddFooterStatus("CW Skimmer stopped.");
            });

        // Click→tune: when user clicks a signal in CW Skimmer, tune the associated slice
        _launcher.FrequencyClicked += freqKhz =>
        {
            var slice = _connection.Slices.FirstOrDefault();
            if (slice is null) return;
            double freqMHz = freqKhz / 1000.0;
            _ = _connection.SetSliceFrequencyAsync(slice, freqMHz);
        };

        // Load persisted settings
        CwSkimmerExePath = _settings.CwSkimmerExePath;

        _discovery.Start();
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    private void OnRadioAdded(DiscoveredRadio radio)   => UIPost(() => { Radios.Add(radio); StatusText = string.Empty; });
    private void OnRadioRemoved(DiscoveredRadio radio) => UIPost(() =>
    {
        var m = Radios.FirstOrDefault(r => r.Serial == radio.Serial);
        if (m is not null) Radios.Remove(m);
        StatusText = Radios.Count == 0 ? "No radios found" : string.Empty;
    });

    // ── Connection ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedRadio is null) return;
        ConnectionStatus = $"Connecting to {SelectedRadio.Model}…";
        bool ok = await _connection.ConnectAsync(SelectedRadio);
        if (!ok) ConnectionStatus = "Connection failed.";
    }

    private bool CanConnect()    => SelectedRadio is not null && !IsConnected;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect() => _connection.Disconnect();

    private bool CanDisconnect() => IsConnected;

    private void OnConnectionStateChanged(bool connected)
    {
        UIPost(() =>
        {
            IsConnected = connected;
            if (connected)
            {
                ConnectionStatus = $"Connected: {_connection.ConnectedModel}  {_connection.Versions}";
                OwnClientStation = _connection.OwnClientStation;
                OnPropertyChanged(nameof(OwnClientStation));

                foreach (var p in _connection.Panadapters)
                    AddPan(p);
                foreach (var s in _connection.Slices)
                    AddSlice(s);
                foreach (var d in _connection.DaxIQStreams)
                    OnDaxIQStreamAdded(d);

                _radioAvgDaxKbps = _connection.AvgDAXKbps;
                UpdateDisplayedDaxKbps();
                AddFooterStatus($"Connected to {_connection.ConnectedModel}.");
            }
            else
            {
                ConnectionStatus = string.Empty;
                OwnClientStation = string.Empty;
                OnPropertyChanged(nameof(OwnClientStation));
                StreamRequestStatus = string.Empty;
                ClientGroups.Clear();
                DaxIQStreams.Clear();
                _radioAvgDaxKbps = 0;
                AvgDAXKbps = 0;
                DaxStreamingSummary = "DAX Streaming : 0.0 Mbps (0 kbps)";
                _lastCwDevicePreviewByChannel.Clear();
                AddFooterStatus("Disconnected.");
            }
        });
    }

    // ── DAX-IQ stream tracking ────────────────────────────────────────────────

    private void OnDaxIQStreamAdded(DaxIQStreamInfo stream)
    {
        var normalized = NormalizeStreamForPan(stream);
        if (DaxIQStreams.All(x => x.DAXIQChannel != normalized.DAXIQChannel))
            DaxIQStreams.Add(normalized);
        else
            ReplaceDaxStream(normalized);

        UpdateDisplayedDaxKbps();
        RefreshAllPanStreamSummaries();
        RefreshCwSkimmerDeviceInfo(normalized.DAXIQChannel);
    }

    private void OnDaxIQStreamRemoved(DaxIQStreamInfo stream)
    {
        var m = DaxIQStreams.FirstOrDefault(x => x.DAXIQChannel == stream.DAXIQChannel);
        if (m is not null) DaxIQStreams.Remove(m);

        UpdateDisplayedDaxKbps();
        RefreshAllPanStreamSummaries();
    }

    private void OnDaxIQStreamUpdated(DaxIQStreamInfo stream)
    {
        var normalized = NormalizeStreamForPan(stream);
        ReplaceDaxStream(normalized);
        UpdateDisplayedDaxKbps();
        RefreshAllPanStreamSummaries();
        RefreshCwSkimmerDeviceInfo(normalized.DAXIQChannel);

        // Sync LO frequency with CW Skimmer if it's running and the centre freq changed
        if (IsCwSkimmerRunning && normalized.CenterFreqMHz > 0)
        {
            var freqHz = (long)(normalized.CenterFreqMHz * 1_000_000);
            _ = _launcher.UpdateLoFreqAsync(freqHz);
        }
    }

    private void RefreshCwSkimmerDeviceInfo(int daxIqChannel)
    {
        var preview = _launcher.PreviewDevices(daxIqChannel);
        var status = preview is null
            ? $"CW device map ch {daxIqChannel}: DAX audio devices not found in WinMM yet."
            : $"CW device map ch {daxIqChannel}: Signal {preview.Value.SignalDevice} [idx {preview.Value.SignalIdx}]  |  Audio {preview.Value.AudioDevice} [idx {preview.Value.AudioIdx}]";

        if (_lastCwDevicePreviewByChannel.TryGetValue(daxIqChannel, out var existing) && existing == status)
            return;

        _lastCwDevicePreviewByChannel[daxIqChannel] = status;
        AddFooterStatus(status);
    }

    // ── Client / pan / slice grouping ─────────────────────────────────────────

    private void AddPan(PanadapterInfo pan)
    {
        var group = GetOrCreateClientGroup(pan.ClientStation);
        if (group.Panadapters.All(p => p.Pan.StreamId != pan.StreamId))
        {
            var panGroup = new PanSliceGroup(pan);
            UpdatePanStreamSummary(panGroup);
            group.Panadapters.Add(panGroup);
        }
        RefreshDaxStreamPanBindings();
    }

    private void RemovePan(PanadapterInfo pan)
    {
        var group = ClientGroups.FirstOrDefault(g => g.Station == pan.ClientStation);
        if (group is null) return;
        var entry = group.Panadapters.FirstOrDefault(p => p.Pan.StreamId == pan.StreamId);
        if (entry is not null) group.Panadapters.Remove(entry);
        if (group.Panadapters.Count == 0) ClientGroups.Remove(group);
        RefreshDaxStreamPanBindings();
    }

    private void UpdatePan(PanadapterInfo pan)
    {
        var group = ClientGroups.FirstOrDefault(g => g.Station == pan.ClientStation);
        if (group is null) return;
        var entry = group.Panadapters.FirstOrDefault(p => p.Pan.StreamId == pan.StreamId);
        if (entry is not null)
        {
            entry.Pan = pan;
            UpdatePanStreamSummary(entry);
        }
        RefreshDaxStreamPanBindings();
    }

    private void AddSlice(SliceInfo slice)
    {
        var group = GetOrCreateClientGroup(slice.ClientStation);
        var panEntry = group.Panadapters.FirstOrDefault(p => p.Pan.StreamId == slice.PanadapterStreamId);
        if (panEntry is null) return;
        if (panEntry.Slices.All(s => s.Slice.Letter != slice.Letter))
        {
            var vm = new SliceViewModel(slice);
            panEntry.Slices.Add(vm);
        }
    }

    private void RemoveSlice(SliceInfo slice)
    {
        var group = ClientGroups.FirstOrDefault(g => g.Station == slice.ClientStation);
        if (group is null) return;
        foreach (var panEntry in group.Panadapters)
        {
            var match = panEntry.Slices.FirstOrDefault(s => s.Slice.Letter == slice.Letter);
            if (match is not null) { panEntry.Slices.Remove(match); return; }
        }
    }

    private void UpdateSlice(SliceInfo slice)
    {
        var group = ClientGroups.FirstOrDefault(g => g.Station == slice.ClientStation);
        if (group is null) return;
        foreach (var panEntry in group.Panadapters)
        {
            var vm = panEntry.Slices.FirstOrDefault(s => s.Slice.Letter == slice.Letter);
            if (vm is not null) { vm.Update(slice); break; }
        }

        // Keep CW Skimmer's main window VFO in sync with the slice frequency
        if (IsCwSkimmerRunning && slice.FreqMHz > 0)
            _ = _launcher.UpdateSliceFreqAsync(slice.FreqMHz);
    }

    private ClientGroup GetOrCreateClientGroup(string station)
    {
        var existing = ClientGroups.FirstOrDefault(g => g.Station == station);
        if (existing is not null) return existing;
        var newGroup = new ClientGroup(station);
        ClientGroups.Add(newGroup);
        return newGroup;
    }

    private void RefreshAllPanStreamSummaries()
    {
        foreach (var group in ClientGroups)
        foreach (var panGroup in group.Panadapters)
            UpdatePanStreamSummary(panGroup);
    }

    private void UpdatePanStreamSummary(PanSliceGroup panGroup)
    {
        int ch = panGroup.Pan.DAXIQChannel;
        if (ch <= 0)
        {
            panGroup.StreamSummary = "No DAX-IQ channel assigned.";
            return;
        }

        var stream = DaxIQStreams.FirstOrDefault(s => s.DAXIQChannel == ch);
        if (stream is null)
        {
            var centerHz = (long)(panGroup.Pan.CenterFreqMHz * 1_000_000);
            panGroup.StreamSummary = $"DAX-IQ ch {ch}: Center Frequency {centerHz} Hz, {GetSampleRateForChannel(ch)} kHz, Off";
            return;
        }

        if (stream.CenterFreqMHz > 0)
        {
            var centerHz = (long)(stream.CenterFreqMHz * 1_000_000);
            panGroup.StreamSummary =
                $"DAX-IQ ch {ch}: Center Frequency {centerHz} Hz, {stream.SampleRate / 1000} kHz, {(stream.IsActive ? "Active" : "Inactive")}";
        }
        else
        {
            panGroup.StreamSummary =
                $"DAX-IQ ch {ch}: No Panadapter, {stream.SampleRate / 1000} kHz, {(stream.IsActive ? "Active" : "Inactive")}";
        }
    }

    private void RefreshDaxStreamPanBindings()
    {
        for (int i = 0; i < DaxIQStreams.Count; i++)
            DaxIQStreams[i] = NormalizeStreamForPan(DaxIQStreams[i]);
    }

    private DaxIQStreamInfo NormalizeStreamForPan(DaxIQStreamInfo stream)
    {
        var pan = _connection.Panadapters.FirstOrDefault(p => p.DAXIQChannel == stream.DAXIQChannel);
        return pan is null
            ? stream with
            {
                CenterFreqMHz = 0,
                IsSkimmerRunning = _launcher.IsChannelRunning(stream.DAXIQChannel)
            }
            : stream with
            {
                CenterFreqMHz = pan.CenterFreqMHz,
                IsSkimmerRunning = _launcher.IsChannelRunning(stream.DAXIQChannel)
            };
    }

    private void ReplaceDaxStream(DaxIQStreamInfo stream)
    {
        var existing = DaxIQStreams.FirstOrDefault(x => x.DAXIQChannel == stream.DAXIQChannel);
        if (existing is null)
        {
            DaxIQStreams.Add(stream);
            return;
        }

        var idx = DaxIQStreams.IndexOf(existing);
        DaxIQStreams[idx] = stream;
    }

    private int GetSampleRateForChannel(int daxChannel)
        => DaxIQStreams.FirstOrDefault(s => s.DAXIQChannel == daxChannel)?.SampleRate / 1000 ?? 48;

    private void UpdateDisplayedDaxKbps()
    {
        if (_radioAvgDaxKbps > 0)
        {
            AvgDAXKbps = _radioAvgDaxKbps;
        }
        else
        {
            // Fallback estimate for IQ-only workflows when radio aggregate remains zero.
            // SmartSDR's displayed IQ stream rate includes transport overhead, so this
            // is slightly above the raw 64 bits/sample payload rate.
            const double iqBitsPerSampleWithOverhead = 64.6666667;
            AvgDAXKbps = DaxIQStreams
                .Where(s => s.IsActive)
                .Sum(s => (int)Math.Round((s.SampleRate * iqBitsPerSampleWithOverhead) / 1000.0));
        }

        DaxStreamingSummary = $"DAX Streaming : {AvgDAXKbps / 1000.0:F1} Mbps ({AvgDAXKbps} kbps)";
    }

    // ── CW Skimmer ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanLaunchCwSkimmerForChannel))]
    private async Task LaunchCwSkimmerForChannelAsync(DaxIQStreamInfo? stream)
    {
        await _cwSkimmerWorkflow.LaunchForChannelAsync(stream, CwSkimmerExePath, AddFooterStatus);
    }

    private bool CanLaunchCwSkimmerForChannel(DaxIQStreamInfo? stream) =>
        _cwSkimmerWorkflow.CanLaunch(stream, CwSkimmerExePath);

    [RelayCommand(CanExecute = nameof(CanStopCwSkimmerForChannel))]
    private void StopCwSkimmerForChannel(DaxIQStreamInfo? stream)
    {
        _cwSkimmerWorkflow.StopForChannel(stream, AddFooterStatus);
        LaunchCwSkimmerForChannelCommand.NotifyCanExecuteChanged();
        StopCwSkimmerForChannelCommand.NotifyCanExecuteChanged();
        RefreshDaxStreamPanBindings();
    }

    private bool CanStopCwSkimmerForChannel(DaxIQStreamInfo? stream) =>
        _cwSkimmerWorkflow.CanStop(stream);

    // ── Shutdown ──────────────────────────────────────────────────────────────

    public void Shutdown()
    {
        if (IsCwSkimmerRunning) _launcher.Stop();
        if (IsConnected) _connection.Disconnect();
        _discovery.Stop();
    }

    partial void OnCwSkimmerExePathChanged(string value)
    {
        _settings.CwSkimmerExePath = value;
    }

    private void AddFooterStatus(string message)
    {
        FooterStatusText = _footerStatusBuffer.Add(message);
    }

    private static void UIPost(System.Action action) => Dispatcher.UIThread.Post(action);
}
