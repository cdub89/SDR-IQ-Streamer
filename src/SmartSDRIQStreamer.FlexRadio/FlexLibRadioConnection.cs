using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using Flex.Smoothlake.FlexLib;

namespace SDRIQStreamer.FlexRadio;

/// <summary>
/// Implements <see cref="IRadioConnection"/> using FlexLib's <see cref="Radio"/>.
/// Wraps the synchronous <c>Radio.Connect()</c> on a thread-pool thread so it
/// never blocks the UI thread.
/// </summary>
public sealed class FlexLibRadioConnection : IRadioConnection
{
    private const double SliceNoOpToleranceMHz = 0.000001; // 1 Hz
    private Radio? _radio;

    // ── Connection ───────────────────────────────────────────────────────────

    public bool IsConnected        => _radio?.Connected ?? false;
    public string? ConnectedModel  => _radio?.Model;
    public string? ConnectedSerial => _radio?.Serial;
    public string? Versions        => _radio?.Versions;

    public event Action<bool>? ConnectionStateChanged;

    public async Task<bool> ConnectAsync(DiscoveredRadio radio)
    {
        var flexRadio = API.RadioList.FirstOrDefault(r => r.Serial == radio.Serial);
        if (flexRadio is null) return false;

        flexRadio.PropertyChanged    += OnRadioPropertyChanged;
        flexRadio.PanadapterAdded    += OnPanadapterAdded;
        flexRadio.PanadapterRemoved  += OnPanadapterRemoved;
        flexRadio.SliceAdded         += OnSliceAdded;
        flexRadio.SliceRemoved       += OnSliceRemoved;
        flexRadio.DAXIQStreamAdded   += OnDAXIQStreamAdded;
        flexRadio.DAXIQStreamRemoved += OnDAXIQStreamRemoved;
        _radio = flexRadio;

        bool connected = await System.Threading.Tasks.Task.Run(() => flexRadio.Connect());

        if (!connected)
        {
            UnwireRadioEvents(flexRadio);
            _radio = null;
            return false;
        }

        // Snapshot lists already populated by FlexLib during connect
        foreach (var pan in flexRadio.PanadapterList)
            TrackPanadapter(pan);

        foreach (var slc in flexRadio.SliceList)
            TrackSlice(slc);

        foreach (var iq in flexRadio.DAXIQStreamList)
        {
            iq.PropertyChanged += OnDAXIQStreamPropertyChanged;
            _flexDaxIQStreams[iq.DAXIQChannel] = iq;
            _daxIQStreams[iq.DAXIQChannel] = ToDaxIQStreamInfo(iq);
        }

        return true;
    }

    public void Disconnect()
    {
        if (_radio is null) return;
        var radio = _radio;
        _radio = null;
        UnwireRadioEvents(radio);

        foreach (var pan in _flexPanadapters.Values)
            pan.PropertyChanged -= OnPanadapterPropertyChanged;
        foreach (var slc in _flexSlices.Values)
            slc.PropertyChanged -= OnSlicePropertyChanged;
        foreach (var iq in _flexDaxIQStreams.Values)
            iq.PropertyChanged -= OnDAXIQStreamPropertyChanged;

        radio.Disconnect();
        _panadapters.Clear();
        _flexPanadapters.Clear();
        _slices.Clear();
        _flexSlices.Clear();
        _daxIQStreams.Clear();
        _flexDaxIQStreams.Clear();
        ConnectionStateChanged?.Invoke(false);
    }

    private void UnwireRadioEvents(Radio radio)
    {
        radio.PropertyChanged    -= OnRadioPropertyChanged;
        radio.PanadapterAdded    -= OnPanadapterAdded;
        radio.PanadapterRemoved  -= OnPanadapterRemoved;
        radio.SliceAdded         -= OnSliceAdded;
        radio.SliceRemoved       -= OnSliceRemoved;
        radio.DAXIQStreamAdded   -= OnDAXIQStreamAdded;
        radio.DAXIQStreamRemoved -= OnDAXIQStreamRemoved;
    }

    private void OnRadioPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "Connected":
                ConnectionStateChanged?.Invoke(_radio?.Connected ?? false);
                break;
            case "AvgDAXkbps":
                if (_radio is not null) AvgDAXKbpsChanged?.Invoke(_radio.AvgDAXkbps);
                break;
        }
    }

    // ── Panadapters ──────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<uint, PanadapterInfo> _panadapters   = new();
    private readonly ConcurrentDictionary<uint, Panadapter>     _flexPanadapters = new();

    public IReadOnlyList<PanadapterInfo> Panadapters => _panadapters.Values.ToList();

    public event Action<PanadapterInfo>? PanadapterAdded;
    public event Action<PanadapterInfo>? PanadapterRemoved;
    public event Action<PanadapterInfo>? PanadapterUpdated;

    private void OnPanadapterAdded(Panadapter pan, Waterfall _)
    {
        var info = TrackPanadapter(pan);
        PanadapterAdded?.Invoke(info);
    }

    private void OnPanadapterRemoved(Panadapter pan)
    {
        pan.PropertyChanged -= OnPanadapterPropertyChanged;
        _flexPanadapters.TryRemove(pan.StreamID, out _);
        if (_panadapters.TryRemove(pan.StreamID, out var info))
            PanadapterRemoved?.Invoke(info);
    }

    private PanadapterInfo TrackPanadapter(Panadapter pan)
    {
        pan.PropertyChanged += OnPanadapterPropertyChanged;
        _flexPanadapters[pan.StreamID] = pan;
        var info = ToPanadapterInfo(pan);
        _panadapters[pan.StreamID] = info;
        return info;
    }

    private void OnPanadapterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Panadapter pan) return;
        if (e.PropertyName is not ("CenterFreq" or "DAXIQChannel")) return;

        var info = ToPanadapterInfo(pan);
        _panadapters[pan.StreamID] = info;
        PanadapterUpdated?.Invoke(info);

        // Keep any live stream's centre-freq in sync with its panadapter.
        if (info.DAXIQChannel > 0 &&
            _daxIQStreams.TryGetValue(info.DAXIQChannel, out var existingStream))
        {
            var updated = existingStream with { CenterFreqMHz = info.CenterFreqMHz };
            _daxIQStreams[info.DAXIQChannel] = updated;
            DaxIQStreamUpdated?.Invoke(updated);
        }
    }

    // ── Slices ───────────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, SliceInfo> _slices     = new();
    private readonly ConcurrentDictionary<string, Slice>     _flexSlices = new();

    public IReadOnlyList<SliceInfo> Slices => _slices.Values.ToList();

    public event Action<SliceInfo>? SliceAdded;
    public event Action<SliceInfo>? SliceRemoved;
    public event Action<SliceInfo>? SliceUpdated;

    private void OnSliceAdded(Slice slc)
    {
        var info = TrackSlice(slc);
        SliceAdded?.Invoke(info);
    }

    private void OnSliceRemoved(Slice slc)
    {
        slc.PropertyChanged -= OnSlicePropertyChanged;
        _flexSlices.TryRemove(SliceKey(slc), out _);
        if (_slices.TryRemove(SliceKey(slc), out var info))
            SliceRemoved?.Invoke(info);
    }

    private SliceInfo TrackSlice(Slice slc)
    {
        slc.PropertyChanged += OnSlicePropertyChanged;
        _flexSlices[SliceKey(slc)] = slc;
        var info = ToSliceInfo(slc);
        _slices[SliceKey(slc)] = info;
        return info;
    }

    private void OnSlicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Slice slc) return;
        if (e.PropertyName is not ("Freq" or "DemodMode")) return;

        var info = ToSliceInfo(slc);
        _slices[SliceKey(slc)] = info;
        SliceUpdated?.Invoke(info);
    }

    public Task SetSliceFrequencyAsync(SliceInfo slice, double freqMHz)
    {
        var target = _flexSlices.Values.FirstOrDefault(s =>
            string.Equals(s.Letter, slice.Letter, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ResolveStation(s.ClientHandle), slice.ClientStation, StringComparison.OrdinalIgnoreCase));

        if (target is not null)
        {
            // Avoid unnecessary radio writes when frequency is unchanged (or within jitter tolerance).
            if (Math.Abs(target.Freq - freqMHz) > SliceNoOpToleranceMHz)
                target.Freq = freqMHz;
        }
        return Task.CompletedTask;
    }

    public Task PublishSpotAsync(RadioSpotInfo spot)
    {
        var radio = _radio;
        if (radio is null || !radio.Connected)
            return Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(spot.Callsign) || spot.RxFrequencyMHz <= 0)
            return Task.CompletedTask;

        var flexSpot = new Spot
        {
            Callsign = spot.Callsign.Trim(),
            RXFrequency = spot.RxFrequencyMHz,
            Source = NormalizeSource(spot.Source),
            SpotterCallsign = string.IsNullOrWhiteSpace(spot.SpotterCallsign) ? null : spot.SpotterCallsign.Trim(),
            Comment = string.IsNullOrWhiteSpace(spot.Comment) ? null : spot.Comment,
            Mode = spot.Mode,
            Color = spot.Color,
            BackgroundColor = spot.BackgroundColor,
            LifetimeSeconds = spot.LifetimeSeconds,
            Timestamp = DateTime.UtcNow
        };

        radio.RequestSpot(flexSpot);
        return Task.CompletedTask;
    }

    // ── DAX-IQ Streams ───────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<int, DaxIQStreamInfo> _daxIQStreams = new();
    private readonly ConcurrentDictionary<int, DAXIQStream> _flexDaxIQStreams = new();

    public IReadOnlyList<DaxIQStreamInfo> DaxIQStreams => _daxIQStreams.Values.ToList();

    public event Action<DaxIQStreamInfo>? DaxIQStreamAdded;
    public event Action<DaxIQStreamInfo>? DaxIQStreamRemoved;
    public event Action<DaxIQStreamInfo>? DaxIQStreamUpdated;

    private void OnDAXIQStreamAdded(DAXIQStream iq)
    {
        iq.PropertyChanged += OnDAXIQStreamPropertyChanged;
        _flexDaxIQStreams[iq.DAXIQChannel] = iq;

        var info = ToDaxIQStreamInfo(iq);
        _daxIQStreams[iq.DAXIQChannel] = info;
        DaxIQStreamAdded?.Invoke(info);
    }

    private void OnDAXIQStreamRemoved(DAXIQStream iq)
    {
        iq.PropertyChanged -= OnDAXIQStreamPropertyChanged;
        _flexDaxIQStreams.TryRemove(iq.DAXIQChannel, out _);

        if (_daxIQStreams.TryRemove(iq.DAXIQChannel, out var info))
            DaxIQStreamRemoved?.Invoke(info);
    }

    private void OnDAXIQStreamPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DAXIQStream iq) return;
        if (e.PropertyName is not ("SampleRate" or "IsActive" or "Pan" or "DAXIQChannel")) return;

        // Handle rare channel-number changes by moving the tracked instance key.
        var oldEntry = _flexDaxIQStreams.FirstOrDefault(kvp => ReferenceEquals(kvp.Value, iq));
        if (oldEntry.Value is not null && oldEntry.Key != iq.DAXIQChannel)
            _flexDaxIQStreams.TryRemove(oldEntry.Key, out _);
        _flexDaxIQStreams[iq.DAXIQChannel] = iq;

        var updated = ToDaxIQStreamInfo(iq);
        _daxIQStreams[iq.DAXIQChannel] = updated;
        DaxIQStreamUpdated?.Invoke(updated);
    }

    public async Task<RequestStreamResult> StopDaxIQStreamAsync(PanadapterInfo pan)
    {
        if (_radio is null) return RequestStreamResult.Timeout;

        var stream = _radio.DAXIQStreamList
            .FirstOrDefault(s => s.DAXIQChannel == pan.DAXIQChannel);

        if (stream is null) return RequestStreamResult.NoChannelAssigned;

        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

        void OnRemoved(DAXIQStream iq)
        {
            if (iq.DAXIQChannel == pan.DAXIQChannel)
                tcs.TrySetResult(true);
        }

        _radio.DAXIQStreamRemoved += OnRemoved;
        try
        {
            stream.Close();

            var completed = await System.Threading.Tasks.Task.WhenAny(
                tcs.Task,
                System.Threading.Tasks.Task.Delay(5000));

            return completed == tcs.Task
                ? RequestStreamResult.Success
                : RequestStreamResult.Timeout;
        }
        finally
        {
            _radio.DAXIQStreamRemoved -= OnRemoved;
        }
    }

    public int AvgDAXKbps => _radio?.AvgDAXkbps ?? 0;
    public event Action<int>? AvgDAXKbpsChanged;

    // ── Own client handle ────────────────────────────────────────────────────

    public uint OwnClientHandle    => _radio?.ClientHandle ?? 0;
    public string OwnClientStation => ResolveStation(OwnClientHandle);

    // ── Request DAX-IQ stream ────────────────────────────────────────────────

    public async Task<RequestStreamResult> RequestDaxIQStreamAsync(PanadapterInfo pan)
    {
        if (_radio is null) return RequestStreamResult.Timeout;

        if (pan.DAXIQChannel <= 0)
            return RequestStreamResult.NoChannelAssigned;

        if (_radio.DAXIQStreamList.Any(s => s.DAXIQChannel == pan.DAXIQChannel))
            return RequestStreamResult.StreamAlreadyActive;

        // Use a TCS so we can await the async DAXIQStreamAdded radio event
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

        void OnAdded(DAXIQStream iq)
        {
            if (iq.DAXIQChannel == pan.DAXIQChannel)
                tcs.TrySetResult(true);
        }

        _radio.DAXIQStreamAdded += OnAdded;
        try
        {
            _radio.RequestDAXIQStream(pan.DAXIQChannel);

            var completed = await System.Threading.Tasks.Task.WhenAny(
                tcs.Task,
                System.Threading.Tasks.Task.Delay(5000));

            return completed == tcs.Task
                ? RequestStreamResult.Success
                : RequestStreamResult.Timeout;
        }
        finally
        {
            _radio.DAXIQStreamAdded -= OnAdded;
        }
    }

    // ── Mapping helpers ──────────────────────────────────────────────────────

    private PanadapterInfo ToPanadapterInfo(Panadapter pan) =>
        new(pan.StreamID, pan.CenterFreq, pan.DAXIQChannel, ResolveStation(pan.ClientHandle));

    private SliceInfo ToSliceInfo(Slice slc) =>
        new(slc.Letter    ?? string.Empty,
            slc.DemodMode ?? string.Empty,
            slc.Freq,
            slc.PanadapterStreamID,
            ResolveStation(slc.ClientHandle));

    private DaxIQStreamInfo ToDaxIQStreamInfo(DAXIQStream iq)
    {
        // iq.Pan may be null or unpopulated when the stream is first created.
        // Look up the centre-freq from the already-tracked panadapters instead.
        var centerFreqMHz = _panadapters.Values
            .FirstOrDefault(p => p.DAXIQChannel == iq.DAXIQChannel)
            ?.CenterFreqMHz ?? 0.0;

        return new(iq.DAXIQChannel, iq.SampleRate, iq.IsActive, centerFreqMHz);
    }

    private static string SliceKey(Slice slc)
    {
        var letter = string.IsNullOrWhiteSpace(slc.Letter)
            ? slc.GetHashCode().ToString()
            : slc.Letter!;
        return $"{slc.ClientHandle:X8}:{letter}";
    }

    private string ResolveStation(uint clientHandle)
    {
        var client = _radio?.GuiClients?.FirstOrDefault(c => c.ClientHandle == clientHandle);
        return string.IsNullOrWhiteSpace(client?.Station)
            ? $"0x{clientHandle:X}"
            : client.Station;
    }

    private static string NormalizeSource(string? source)
    {
        var effective = string.IsNullOrWhiteSpace(source) ? "CWSkimmer" : source.Trim();
        return effective.Replace(' ', '_');
    }
}
