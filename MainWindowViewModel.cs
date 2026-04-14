using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDRIQStreamer.CWSkimmer;
using SDRIQStreamer.FlexRadio;

namespace SDRIQStreamer.App;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly object s_streamerLogSync = new();
    private static readonly object s_spotPayloadLogSync = new();
    private readonly IRadioDiscovery   _discovery;
    private readonly IRadioConnection  _connection;
    private readonly ICwSkimmerLauncher _launcher;
    private readonly AppSettingsSession _settingsSession;
    private readonly AppSettings _settings;
    private readonly FooterStatusBuffer _footerStatusBuffer;
    private readonly CwSkimmerWorkflowService _cwSkimmerWorkflow;
    private static readonly (string ReleaseTag, string CommitHash, string Display) s_appBuildInfo =
        ResolveAppBuildInfo();

    // ── Discovered radios ─────────────────────────────────────────────────────

    public ObservableCollection<DiscoveredRadio> Radios { get; } = new();
    public ObservableCollection<RadioConnectTarget> ConnectTargets { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor("ConnectCommand")]
    private RadioConnectTarget? _selectedConnectTarget;

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
    public IEnumerable<ClientGroup> VisibleClientGroups =>
        string.IsNullOrWhiteSpace(SelectedControlStation)
            ? ClientGroups
            : ClientGroups.Where(g => string.Equals(g.Station, SelectedControlStation, StringComparison.OrdinalIgnoreCase));

    public ObservableCollection<DaxIQStreamInfo> DaxIQStreams { get; } = new();

    [ObservableProperty]
    private int _avgDAXKbps;
    private int _radioAvgDaxKbps;

    [ObservableProperty]
    private string _daxStreamingSummary = "DAX Streaming : 0.0 Mbps (0 kbps)";

    [ObservableProperty]
    private string _streamRequestStatus = string.Empty;

    /// <summary>Station name of our own connected client.</summary>
    public string OwnClientStation { get; private set; } = string.Empty;
    public string SelectedControlStation { get; private set; } = string.Empty;

    // ── CW Skimmer ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCwSkimmerForChannelCommand))]
    private string _cwSkimmerExePath = string.Empty;

    [ObservableProperty]
    private string _cwSkimmerIniPath = string.Empty;

    [ObservableProperty]
    private bool _isCwSkimmerRunning;

    [ObservableProperty]
    private string _telnetCallsign = string.Empty;

    [ObservableProperty]
    private int _connectDelaySeconds = 5;

    [ObservableProperty]
    private int _launchDelaySeconds = 3;

    [ObservableProperty]
    private int _telnetPortBase = 7300;

    [ObservableProperty]
    private bool _telnetClusterEnabled = true;

    [ObservableProperty]
    private string _telnetIniSummaryText = string.Empty;

    [ObservableProperty]
    private bool _spotForwardingEnabled = true;

    [ObservableProperty]
    private int _spotLifetimeSeconds = 300;

    [ObservableProperty]
    private string _spotColor = "#FF00FFFF";

    [ObservableProperty]
    private string _spotBackgroundColor = "#00000000";

    [ObservableProperty]
    private SpotColorOption? _spotSelectedColorOption;
    public IReadOnlyList<SpotColorOption> SpotColorOptions { get; } =
    [
        new SpotColorOption("Red", "#FFFF0000"),
        new SpotColorOption("Green", "#FF008000"),
        new SpotColorOption("Blue", "#FF0000FF"),
        new SpotColorOption("Yellow", "#FFFFFF00"),
        new SpotColorOption("Orange", "#FFFFA500"),
        new SpotColorOption("Purple", "#FF800080"),
        new SpotColorOption("Cyan", "#FF00FFFF"),
        new SpotColorOption("White", "#FFFFFFFF"),
    ];

    [ObservableProperty]
    private SpotColorOption? _spotSelectedBackgroundColorOption;
    public IReadOnlyList<SpotColorOption> SpotBackgroundColorOptions { get; } =
    [
        new SpotColorOption("Transparent", "#00000000"),
        new SpotColorOption("Red", "#66FF0000"),
        new SpotColorOption("Green", "#6600FF00"),
        new SpotColorOption("Blue", "#660000FF"),
        new SpotColorOption("Yellow", "#66FFFF00"),
        new SpotColorOption("Orange", "#66FFA500"),
        new SpotColorOption("Purple", "#66800080"),
        new SpotColorOption("Cyan", "#6600FFFF"),
    ];

    public ObservableCollection<string> FooterStatusLines { get; } = new();
    private readonly Dictionary<int, string> _lastCwDevicePreviewByChannel = new();
    private readonly Dictionary<int, long> _lastLoCenterHzByChannel = new();
    private CancellationTokenSource? _sliceSyncCts;
    private readonly object _syncDampenGate = new();
    private double _lastOutboundQsyMHz;
    private DateTime _lastOutboundQsyUtc;
    private double _lastInboundClickMHz;
    private DateTime _lastInboundClickUtc;

    private const double EchoSuppressToleranceMHz = 0.000010; // 10 Hz
    private static readonly TimeSpan EchoSuppressWindow = TimeSpan.FromMilliseconds(700);
    private const double DuplicateClickToleranceMHz = 0.000005; // 5 Hz
    private static readonly TimeSpan DuplicateClickWindow = TimeSpan.FromMilliseconds(250);
    [ObservableProperty]
    private string _footerStatusText = string.Empty;

    [ObservableProperty]
    private string _latestFooterEvent = string.Empty;

    public string AppReleaseTag => s_appBuildInfo.ReleaseTag;
    public string AppCommitHash => s_appBuildInfo.CommitHash;
    public string AppBuildDisplay => s_appBuildInfo.Display;
    public string WindowTitle => $"SDR-IQ-Streamer {AppBuildDisplay}";

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
                    AddSkimmerStatus("CW Skimmer stopped.");
            });

        // Click→tune: when user clicks a signal in CW Skimmer, tune the associated slice
        _launcher.FrequencyClicked += freqKhz =>
        {
            var slice = GetPreferredSliceForTune();
            if (slice is null) return;
            double freqMHz = freqKhz / 1000.0;
            if (ShouldSuppressInboundClick(freqMHz))
                return;

            _ = _connection.SetSliceFrequencyAsync(slice, freqMHz);
            UIPost(() => AddTelnetStatus(
                $"Click tune (Skimmer): {freqMHz:F6} MHz -> Slice {slice.Letter} ({slice.ClientStation})"));
        };

        _launcher.TelnetStatusChanged += message =>
            UIPost(() => AddTelnetStatus(message));
        _launcher.SpotReceived += spot => _ = PublishSkimmerSpotAsync(spot);

        // Load persisted settings
        CwSkimmerExePath = _settings.CwSkimmerExePath;
        CwSkimmerIniPath = _settings.CwSkimmerIniPath;
        TelnetCallsign = _settings.Callsign;
        ConnectDelaySeconds = _settings.ConnectDelaySeconds;
        LaunchDelaySeconds = _settings.LaunchDelaySeconds;
        TelnetPortBase = _settings.TelnetPortBase;
        TelnetClusterEnabled = _settings.TelnetClusterEnabled;
        UpdateTelnetIniSummary();
        SpotForwardingEnabled = _settings.SpotForwardingEnabled;
        SpotLifetimeSeconds = _settings.SpotLifetimeSeconds;
        SpotColor = _settings.SpotColor;
        SpotBackgroundColor = _settings.SpotBackgroundColor;
        UpdateSpotColorSelection(SpotColor);
        UpdateSpotBackgroundColorSelection(SpotBackgroundColor);

        AddStreamerStatus($"Release: {AppReleaseTag} | Commit: {AppCommitHash}");
        _discovery.Start();
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    private void OnRadioAdded(DiscoveredRadio radio) => UIPost(() =>
    {
        var existing = Radios.FirstOrDefault(r => r.Serial == radio.Serial);
        if (existing is null)
            Radios.Add(radio);
        else
            Radios[Radios.IndexOf(existing)] = radio;

        RebuildConnectTargets();
        StatusText = Radios.Count == 0 ? "No radios found" : string.Empty;
    });

    private void OnRadioRemoved(DiscoveredRadio radio) => UIPost(() =>
    {
        var m = Radios.FirstOrDefault(r => r.Serial == radio.Serial);
        if (m is not null) Radios.Remove(m);
        RebuildConnectTargets();
        StatusText = Radios.Count == 0 ? "No radios found" : string.Empty;
    });

    // ── Connection ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedConnectTarget is null) return;

        SetSelectedControlStation(SelectedConnectTarget.Station);
        ConnectionStatus = $"Connecting to {SelectedConnectTarget.Radio.Model} ({SelectedControlStation})…";
        bool ok = await _connection.ConnectAsync(SelectedConnectTarget.Radio);
        if (!ok) ConnectionStatus = "Connection failed.";
    }

    private bool CanConnect() => SelectedConnectTarget is not null && !IsConnected;

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
                EnsureSelectedControlStation();

                foreach (var p in _connection.Panadapters)
                    AddPan(p);
                foreach (var s in _connection.Slices)
                    AddSlice(s);
                foreach (var d in _connection.DaxIQStreams)
                    OnDaxIQStreamAdded(d);

                _radioAvgDaxKbps = _connection.AvgDAXKbps;
                UpdateDisplayedDaxKbps();
                AddStreamerStatus($"Connected to {_connection.ConnectedModel}.");
                AddStreamerStatus($"Control station: {SelectedControlStation}");
            }
            else
            {
                ConnectionStatus = string.Empty;
                OwnClientStation = string.Empty;
                OnPropertyChanged(nameof(OwnClientStation));
                SetSelectedControlStation(string.Empty);
                StreamRequestStatus = string.Empty;
                ClientGroups.Clear();
                DaxIQStreams.Clear();
                _radioAvgDaxKbps = 0;
                AvgDAXKbps = 0;
                DaxStreamingSummary = "DAX Streaming : 0.0 Mbps (0 kbps)";
                _lastCwDevicePreviewByChannel.Clear();
                _lastLoCenterHzByChannel.Clear();
                AddStreamerStatus("Disconnected.");
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

        // Adaptive pan-center LO sync: only re-center CW Skimmer when pan center
        // moves beyond half the stream sample-rate bandwidth.
        if (TelnetClusterEnabled &&
            IsCwSkimmerRunning &&
            normalized.CenterFreqMHz > 0 &&
            normalized.SampleRate > 0 &&
            IsOwnStationPanChannel(normalized.DAXIQChannel))
        {
            var centerHz = (long)Math.Round(normalized.CenterFreqMHz * 1_000_000d);
            var halfBandwidthHz = normalized.SampleRate / 2L;

            if (ShouldResyncLoForPanCenterMove(normalized.DAXIQChannel, centerHz, halfBandwidthHz))
            {
                _ = _launcher.UpdateLoFreqAsync(centerHz);
                AddTelnetStatus($"Adaptive LO sync: ch {normalized.DAXIQChannel} center moved > {halfBandwidthHz} Hz.");
            }
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
        AddSkimmerStatus(status);
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
        OnPropertyChanged(nameof(VisibleClientGroups));
    }

    private void RemovePan(PanadapterInfo pan)
    {
        var group = ClientGroups.FirstOrDefault(g => g.Station == pan.ClientStation);
        if (group is null) return;
        var entry = group.Panadapters.FirstOrDefault(p => p.Pan.StreamId == pan.StreamId);
        if (entry is not null) group.Panadapters.Remove(entry);
        if (group.Panadapters.Count == 0) ClientGroups.Remove(group);
        RefreshDaxStreamPanBindings();
        OnPropertyChanged(nameof(VisibleClientGroups));
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
        OnPropertyChanged(nameof(VisibleClientGroups));
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
        OnPropertyChanged(nameof(VisibleClientGroups));
    }

    private void RemoveSlice(SliceInfo slice)
    {
        var group = ClientGroups.FirstOrDefault(g => g.Station == slice.ClientStation);
        if (group is null) return;
        var removed = false;
        foreach (var panEntry in group.Panadapters)
        {
            var match = panEntry.Slices.FirstOrDefault(s => s.Slice.Letter == slice.Letter);
            if (match is null) continue;
            panEntry.Slices.Remove(match);
            removed = true;
            break;
        }
        if (removed)
            OnPropertyChanged(nameof(VisibleClientGroups));
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
        if (TelnetClusterEnabled &&
            IsCwSkimmerRunning &&
            slice.FreqMHz > 0 &&
            IsOwnStationSlice(slice))
            QueueSliceSync(slice.FreqMHz);
    }

    private ClientGroup GetOrCreateClientGroup(string station)
    {
        var existing = ClientGroups.FirstOrDefault(g => g.Station == station);
        if (existing is not null) return existing;
        var newGroup = new ClientGroup(station);
        ClientGroups.Add(newGroup);
        OnPropertyChanged(nameof(VisibleClientGroups));
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
        await _cwSkimmerWorkflow.LaunchForChannelAsync(stream, CwSkimmerExePath, AddSkimmerStatus);
    }

    private bool CanLaunchCwSkimmerForChannel(DaxIQStreamInfo? stream) =>
        _cwSkimmerWorkflow.CanLaunch(stream, CwSkimmerExePath);

    [RelayCommand(CanExecute = nameof(CanStopCwSkimmerForChannel))]
    private void StopCwSkimmerForChannel(DaxIQStreamInfo? stream)
    {
        _cwSkimmerWorkflow.StopForChannel(stream, AddSkimmerStatus);
        LaunchCwSkimmerForChannelCommand.NotifyCanExecuteChanged();
        StopCwSkimmerForChannelCommand.NotifyCanExecuteChanged();
        RefreshDaxStreamPanBindings();
    }

    private bool CanStopCwSkimmerForChannel(DaxIQStreamInfo? stream) =>
        _cwSkimmerWorkflow.CanStop(stream);

    private async Task PublishSkimmerSpotAsync(CwSkimmerSpotInfo spot)
    {
        if (!IsConnected || !SpotForwardingEnabled || spot.FrequencyKhz <= 0 || string.IsNullOrWhiteSpace(spot.Callsign))
            return;

        var commentParts = new List<string>();
        if (spot.SignalDb.HasValue)
            commentParts.Add($"{spot.SignalDb.Value} dB");
        if (spot.SpeedWpm.HasValue)
            commentParts.Add($"{spot.SpeedWpm.Value} WPM");
        if (!string.IsNullOrWhiteSpace(spot.Comment))
            commentParts.Add(spot.Comment);

        var comment = commentParts.Count == 0 ? null : string.Join(" | ", commentParts);
        var sourceBaseCall = ResolveSourceBaseCall(spot.Spotter);
        var sourceIdentity = ResolveSpotSourceIdentity(sourceBaseCall);

        var radioSpot = new RadioSpotInfo(
            Callsign: spot.Callsign,
            RxFrequencyMHz: spot.FrequencyKhz / 1000.0,
            Source: sourceIdentity,
            SpotterCallsign: sourceBaseCall,
            Comment: comment,
            Mode: "CW",
            Color: NormalizeSpotColor(SpotColor),
            BackgroundColor: NormalizeSpotBackgroundColor(SpotBackgroundColor),
            LifetimeSeconds: Math.Max(30, SpotLifetimeSeconds));

        var payloadSummary =
            $"call={radioSpot.Callsign}, freq_mhz={radioSpot.RxFrequencyMHz:F6}, source={radioSpot.Source}, " +
            $"spotter_callsign={radioSpot.SpotterCallsign}, lifetime_seconds={radioSpot.LifetimeSeconds}";

        try
        {
            AppendSpotPayloadLog($"publish-attempt {payloadSummary}");
            await _connection.PublishSpotAsync(radioSpot);
            AppendSpotPayloadLog($"publish-success {payloadSummary}");
            UIPost(() => AddSkimmerStatus(
                $"Spot sent: {spot.Callsign} @ {(spot.FrequencyKhz / 1000.0):F6} MHz"));
        }
        catch (Exception ex)
        {
            AppendSpotPayloadLog($"publish-failed {payloadSummary}, error={ex.Message}");
            UIPost(() => AddSkimmerStatus($"Spot publish failed: {ex.Message}"));
        }
    }

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

    partial void OnCwSkimmerIniPathChanged(string value)
    {
        _settings.CwSkimmerIniPath = value;
    }

    partial void OnTelnetCallsignChanged(string value)
    {
        _settings.Callsign = value;
        UpdateTelnetIniSummary();
    }

    partial void OnConnectDelaySecondsChanged(int value)
    {
        _settings.ConnectDelaySeconds = Math.Max(0, value);
    }

    partial void OnLaunchDelaySecondsChanged(int value)
    {
        _settings.LaunchDelaySeconds = Math.Max(0, value);
    }

    partial void OnTelnetPortBaseChanged(int value)
    {
        _settings.TelnetPortBase = Math.Max(1, value);
        UpdateTelnetIniSummary();
    }

    partial void OnTelnetClusterEnabledChanged(bool value)
    {
        _settings.TelnetClusterEnabled = value;
        UpdateTelnetIniSummary();
    }

    partial void OnSpotForwardingEnabledChanged(bool value)
    {
        _settings.SpotForwardingEnabled = value;
    }

    partial void OnSpotLifetimeSecondsChanged(int value)
    {
        _settings.SpotLifetimeSeconds = Math.Max(30, value);
    }

    partial void OnSpotColorChanged(string value)
    {
        var normalized = NormalizeSpotColor(value);
        _settings.SpotColor = normalized;
        UpdateSpotColorSelection(normalized);
    }

    partial void OnSpotBackgroundColorChanged(string value)
    {
        var normalized = NormalizeSpotBackgroundColor(value);
        _settings.SpotBackgroundColor = normalized;
        UpdateSpotBackgroundColorSelection(normalized);
    }

    partial void OnSpotSelectedColorOptionChanged(SpotColorOption? value)
    {
        if (value is null)
            return;

        if (!string.Equals(SpotColor, value.Hex, StringComparison.OrdinalIgnoreCase))
            SpotColor = value.Hex;
    }

    partial void OnSpotSelectedBackgroundColorOptionChanged(SpotColorOption? value)
    {
        if (value is null)
            return;

        if (!string.Equals(SpotBackgroundColor, value.Hex, StringComparison.OrdinalIgnoreCase))
            SpotBackgroundColor = value.Hex;
    }

    private void AddFooterStatus(string message)
    {
        LatestFooterEvent = message;
        FooterStatusText = _footerStatusBuffer.Add(message);
    }

    private void AddStreamerStatus(string message)
    {
        AddFooterStatus($"[STREAMER] {message}");
        AppendStreamerLog(message);
    }
    private void AddSkimmerStatus(string message) => AddFooterStatus($"[SKIMMER] {message}");
    private void AddTelnetStatus(string message) => AddFooterStatus($"[TELNET] {message}");

    private static void AppendStreamerLog(string message)
    {
        try
        {
            var logPath = ResolveStreamerLogPath();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [STREAMER] {message}{Environment.NewLine}";

            lock (s_streamerLogSync)
            {
                var dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(logPath, line);
            }
        }
        catch
        {
            // Logging must not impact runtime behavior.
        }
    }

    private static void AppendSpotPayloadLog(string message)
    {
        try
        {
            var logPath = ResolveSpotPayloadLogPath();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [SPOT] {message}{Environment.NewLine}";

            lock (s_spotPayloadLogSync)
            {
                var dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(logPath, line);
            }
        }
        catch
        {
            // Logging must not impact runtime behavior.
        }
    }

    private static string ResolveStreamerLogPath()
    {
        var repoRoot = TryFindRepoRoot(new DirectoryInfo(AppContext.BaseDirectory))
            ?? TryFindRepoRoot(new DirectoryInfo(Environment.CurrentDirectory));

        if (repoRoot is not null)
            return Path.Combine(repoRoot.FullName, "artifacts", "logs", "streamer-status.log");

        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SDRIQStreamer");
        return Path.Combine(appDataRoot, "artifacts", "logs", "streamer-status.log");
    }

    private static string ResolveSpotPayloadLogPath()
    {
        var repoRoot = TryFindRepoRoot(new DirectoryInfo(AppContext.BaseDirectory))
            ?? TryFindRepoRoot(new DirectoryInfo(Environment.CurrentDirectory));

        if (repoRoot is not null)
            return Path.Combine(repoRoot.FullName, "artifacts", "logs", "spot-publish.log");

        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SDRIQStreamer");
        return Path.Combine(appDataRoot, "artifacts", "logs", "spot-publish.log");
    }

    private static DirectoryInfo? TryFindRepoRoot(DirectoryInfo? start)
    {
        var current = start;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SmartSDRIQStreamer.csproj")) ||
                File.Exists(Path.Combine(current.FullName, "SmartSDRIQStreamer.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string NormalizeSpotColor(string color)
    {
        var trimmed = string.IsNullOrWhiteSpace(color)
            ? string.Empty
            : color.Trim().ToUpperInvariant();

        if (trimmed.Length == 9 && trimmed.StartsWith("#") &&
            trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            return trimmed;
        }

        return "#FF00FFFF";
    }

    private static string NormalizeSpotBackgroundColor(string color)
    {
        var trimmed = string.IsNullOrWhiteSpace(color)
            ? string.Empty
            : color.Trim().ToUpperInvariant();

        if (trimmed.Length == 9 && trimmed.StartsWith("#") &&
            trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            return trimmed;
        }

        return "#00000000";
    }

    private void UpdateSpotColorSelection(string colorHex)
    {
        var normalized = NormalizeSpotColor(colorHex);
        var match = SpotColorOptions.FirstOrDefault(c =>
            string.Equals(c.Hex, normalized, StringComparison.OrdinalIgnoreCase));

        match ??= SpotColorOptions.FirstOrDefault(c => c.Name == "Cyan");
        if (match is not null && !Equals(SpotSelectedColorOption, match))
            SpotSelectedColorOption = match;
    }

    private void UpdateSpotBackgroundColorSelection(string colorHex)
    {
        var normalized = NormalizeSpotBackgroundColor(colorHex);
        var match = SpotBackgroundColorOptions.FirstOrDefault(c =>
            string.Equals(c.Hex, normalized, StringComparison.OrdinalIgnoreCase));

        match ??= SpotBackgroundColorOptions.FirstOrDefault(c => c.Name == "Transparent");
        if (match is not null && !Equals(SpotSelectedBackgroundColorOption, match))
            SpotSelectedBackgroundColorOption = match;
    }

    private void UpdateTelnetIniSummary()
    {
        if (TryReadLatestTelnetIniSection(out var sourceFileName, out var telnetLines))
        {
            TelnetIniSummaryText = string.Join(Environment.NewLine,
            [
                $"INI file: {sourceFileName}",
                "",
                "[Telnet]",
                .. telnetLines
            ]);
            return;
        }

        var callsign = string.IsNullOrWhiteSpace(TelnetCallsign) ? "(auto)" : TelnetCallsign.Trim();
        var portBase = Math.Max(1, TelnetPortBase);
        TelnetIniSummaryText = string.Join(Environment.NewLine,
        [
            "INI file: (none generated yet)",
            "",
            "[Telnet]",
            $"Port={portBase} + (DAXIQ channel x 10)",
            "PasswordRequired=0",
            "Password=",
            "CqOnly=0",
            "AllowAnn=1",
            "AnnUserOnly=0",
            "AnnUser=",
            "TelnetSrvEnabled=1",
            "UdpSourceName=CW Skimmer",
            "UdpAddress=127.0.0.1",
            "UdpPort=13064",
            "UdpEnabled=0",
            "",
            $"LoginCallsign={callsign}",
            $"ClusterCommands={(TelnetClusterEnabled ? 1 : 0)}",
        ]);
    }

    private static bool TryReadLatestTelnetIniSection(out string sourceFileName, out IReadOnlyList<string> telnetLines)
    {
        sourceFileName = string.Empty;
        telnetLines = [];

        try
        {
            var iniDir = ResolveCwSkimmerIniDirPath();
            if (!Directory.Exists(iniDir))
                return false;

            var iniFile = new DirectoryInfo(iniDir)
                .GetFiles("CwSkimmer-ch*.ini", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (iniFile is null)
                return false;

            sourceFileName = iniFile.Name;
            var lines = File.ReadAllLines(iniFile.FullName);
            var start = Array.FindIndex(lines, l =>
                string.Equals(l.Trim(), "[Telnet]", StringComparison.OrdinalIgnoreCase));
            if (start < 0)
                return false;

            var captured = new List<string>();
            for (var i = start + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                    break;
                if (line.Length == 0)
                    continue;
                captured.Add(line);
            }

            telnetLines = captured.Count == 0 ? [] : captured;
            return captured.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveCwSkimmerIniDirPath()
    {
        var repoRoot = TryFindRepoRoot(new DirectoryInfo(AppContext.BaseDirectory))
            ?? TryFindRepoRoot(new DirectoryInfo(Environment.CurrentDirectory));

        if (repoRoot is not null)
            return Path.Combine(repoRoot.FullName, "artifacts", "cwskimmer", "ini");

        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SDRIQStreamer");
        return Path.Combine(appDataRoot, "artifacts", "cwskimmer", "ini");
    }

    private static (string ReleaseTag, string CommitHash, string Display) ResolveAppBuildInfo()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var releaseTag = ResolveReleaseTag(assembly, informationalVersion) ?? "dev";

        var commit = ExtractCommitHash(informationalVersion)
            ?? ExtractCommitHash(
                assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(x =>
                        string.Equals(x.Key, "SourceRevisionId", StringComparison.OrdinalIgnoreCase))
                    ?.Value)
            ?? "unknown";

        var display = commit == "unknown"
            ? $"v{releaseTag}"
            : $"v{releaseTag} ({commit})";
        return (releaseTag, commit, display);
    }

    private static string? ResolveReleaseTag(Assembly assembly, string? informationalVersion)
    {
        // Prefer explicit git tags so runtime log reflects release labels like alpha.2/alpha.3.
        var exactTag = TryGetGitTag(pointsAtHead: true);
        if (!string.IsNullOrWhiteSpace(exactTag))
            return exactTag;

        var latestTag = TryGetGitTag(pointsAtHead: false);
        if (!string.IsNullOrWhiteSpace(latestTag))
            return latestTag;

        return ExtractVersion(informationalVersion)
            ?? assembly.GetName().Version?.ToString(3);
    }

    private static string? ExtractVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return null;

        var plusIndex = informationalVersion.IndexOf('+');
        var version = plusIndex >= 0
            ? informationalVersion[..plusIndex]
            : informationalVersion;

        version = version.Trim();
        return version.Length == 0 ? null : version;
    }

    private static string? ExtractCommitHash(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var match = Regex.Match(source, "[0-9a-fA-F]{7,40}");
        if (!match.Success)
            return null;

        var hash = match.Value.ToLowerInvariant();
        return hash.Length <= 8 ? hash : hash[..8];
    }

    private static string? TryGetGitTag(bool pointsAtHead)
    {
        try
        {
            var repoRoot = TryFindRepoRoot(new DirectoryInfo(AppContext.BaseDirectory))
                ?? TryFindRepoRoot(new DirectoryInfo(Environment.CurrentDirectory));
            var workingDir = repoRoot?.FullName ?? Environment.CurrentDirectory;

            var args = pointsAtHead
                ? "tag --points-at HEAD --sort=-v:refname"
                : "describe --tags --abbrev=0";

            var result = RunGit(args, workingDir);
            if (string.IsNullOrWhiteSpace(result))
                return null;

            if (!pointsAtHead)
                return result;

            // If multiple tags point at HEAD, prefer the first sorted highest version.
            var firstLine = result
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? RunGit(string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(2000);
        if (process.ExitCode != 0)
            return null;

        return output.Trim();
    }

    private void QueueSliceSync(double freqMHz)
    {
        if (!TelnetClusterEnabled)
            return;

        var cts = ReplaceSyncCts(ref _sliceSyncCts);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, cts.Token);
                RecordOutboundQsy(freqMHz);
                await _launcher.UpdateSliceFreqAsync(freqMHz);
            }
            catch (OperationCanceledException) { }
        });
    }

    private static CancellationTokenSource ReplaceSyncCts(ref CancellationTokenSource? field)
    {
        var next = new CancellationTokenSource();
        var previous = field;
        field = next;

        if (previous is not null)
        {
            try { previous.Cancel(); } catch { }
            previous.Dispose();
        }

        return next;
    }

    private void RecordOutboundQsy(double freqMHz)
    {
        lock (_syncDampenGate)
        {
            _lastOutboundQsyMHz = freqMHz;
            _lastOutboundQsyUtc = DateTime.UtcNow;
        }
    }

    private bool ShouldSuppressInboundClick(double freqMHz)
    {
        var now = DateTime.UtcNow;
        lock (_syncDampenGate)
        {
            // Suppress immediate echo-back from our own outbound QSY commands.
            if ((now - _lastOutboundQsyUtc) <= EchoSuppressWindow &&
                Math.Abs(freqMHz - _lastOutboundQsyMHz) <= EchoSuppressToleranceMHz)
            {
                return true;
            }

            // Suppress fast duplicate click notifications at effectively same frequency.
            if ((now - _lastInboundClickUtc) <= DuplicateClickWindow &&
                Math.Abs(freqMHz - _lastInboundClickMHz) <= DuplicateClickToleranceMHz)
            {
                return true;
            }

            _lastInboundClickMHz = freqMHz;
            _lastInboundClickUtc = now;
            return false;
        }
    }

    private SliceInfo? GetPreferredSliceForTune()
    {
        var ownSlice = _connection.Slices.FirstOrDefault(IsOwnStationSlice);
        return ownSlice ?? _connection.Slices.FirstOrDefault();
    }

    private string ResolveSpotSourceIdentity(string baseCall)
    {
        var sliceLetter = GetPreferredSliceForTune()?.Letter?.Trim();
        if (string.IsNullOrWhiteSpace(sliceLetter))
            return baseCall;

        return $"{baseCall}/{sliceLetter.ToUpperInvariant()}";
    }

    private string ResolveSourceBaseCall(string? spotterFromTelnet)
    {
        if (IsLikelySourceCallsign(_settings.Callsign))
            return _settings.Callsign.Trim().ToUpperInvariant();

        var normalizedSpotter = NormalizeTelnetSpotterCallsign(spotterFromTelnet);
        if (IsLikelySourceCallsign(normalizedSpotter))
            return normalizedSpotter;

        return "CWSKIMMER";
    }

    private static string NormalizeTelnetSpotterCallsign(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim().ToUpperInvariant();
        var hyphenIdx = trimmed.LastIndexOf('-');
        if (hyphenIdx <= 0 || hyphenIdx >= trimmed.Length - 1)
            return trimmed;

        var suffix = trimmed[(hyphenIdx + 1)..];
        if (suffix.All(ch => ch == '#' || char.IsDigit(ch)))
            return trimmed[..hyphenIdx];

        return trimmed;
    }

    private static bool IsLikelySourceCallsign(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length < 3 || trimmed.Length > 16)
            return false;

        bool hasLetter = false;
        bool hasDigit = false;
        foreach (var ch in trimmed)
        {
            if (char.IsLetter(ch)) hasLetter = true;
            else if (char.IsDigit(ch)) hasDigit = true;
            else if (ch != '-' && ch != '/')
                return false;
        }

        return hasLetter && hasDigit;
    }

    private bool IsOwnStationSlice(SliceInfo slice)
    {
        if (string.IsNullOrWhiteSpace(SelectedControlStation))
            return false;

        return string.Equals(slice.ClientStation, SelectedControlStation, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsOwnStationPanChannel(int daxIqChannel)
    {
        if (string.IsNullOrWhiteSpace(SelectedControlStation))
            return false;

        return _connection.Panadapters.Any(p =>
            p.DAXIQChannel == daxIqChannel &&
            string.Equals(p.ClientStation, SelectedControlStation, StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldResyncLoForPanCenterMove(int channel, long centerHz, long halfBandwidthHz)
    {
        if (!_lastLoCenterHzByChannel.TryGetValue(channel, out var previousHz))
        {
            _lastLoCenterHzByChannel[channel] = centerHz;
            return false;
        }

        if (Math.Abs(centerHz - previousHz) <= Math.Max(1, halfBandwidthHz))
            return false;

        _lastLoCenterHzByChannel[channel] = centerHz;
        return true;
    }

    private void EnsureSelectedControlStation()
    {
        if (string.Equals(SelectedControlStation, RadioConnectTarget.UnknownStation, StringComparison.OrdinalIgnoreCase))
            SetSelectedControlStation(string.Empty);

        if (!string.IsNullOrWhiteSpace(SelectedControlStation))
            return;

        if (!string.IsNullOrWhiteSpace(OwnClientStation))
        {
            SetSelectedControlStation(OwnClientStation);
            return;
        }

        SetSelectedControlStation(_connection.Slices
            .Select(s => s.ClientStation)
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
            ?? string.Empty);
    }

    private void SetSelectedControlStation(string station)
    {
        if (string.Equals(SelectedControlStation, station, StringComparison.OrdinalIgnoreCase))
            return;

        SelectedControlStation = station;
        OnPropertyChanged(nameof(SelectedControlStation));
        OnPropertyChanged(nameof(VisibleClientGroups));
    }

    private void RebuildConnectTargets()
    {
        var selected = SelectedConnectTarget;
        ConnectTargets.Clear();

        foreach (var radio in Radios.OrderBy(r => r.Model).ThenBy(r => r.Nickname).ThenBy(r => r.Serial))
        {
            var stations = radio.Stations
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (stations.Length == 0)
            {
                ConnectTargets.Add(new RadioConnectTarget(radio, RadioConnectTarget.UnknownStation));
                continue;
            }

            foreach (var station in stations)
                ConnectTargets.Add(new RadioConnectTarget(radio, station));
        }

        if (selected is null)
        {
            if (ConnectTargets.Count > 0)
                SelectedConnectTarget = ConnectTargets[0];
            return;
        }

        SelectedConnectTarget = ConnectTargets.FirstOrDefault(t =>
            t.Radio.Serial == selected.Radio.Serial &&
            string.Equals(t.Station, selected.Station, StringComparison.OrdinalIgnoreCase))
            ?? ConnectTargets.FirstOrDefault();
    }

    private static void UIPost(System.Action action) => Dispatcher.UIThread.Post(action);
}

public sealed record SpotColorOption(string Name, string Hex);
