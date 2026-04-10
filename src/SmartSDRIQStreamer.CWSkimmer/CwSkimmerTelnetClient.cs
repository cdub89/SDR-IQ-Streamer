using System.Net.Sockets;
using System.Text;

namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Line-based TCP client for CW Skimmer's telnet server.
///
/// Login sequence (from SliceMaster reference):
///   ← server sends greeting ending with "Please enter your callsign: "
///   → client sends callsign\r\n
///   ← server may send "Please enter your password: "
///   → client sends password\r\n
///
/// After login, receive loop parses:
///   "To ALL de SKIMMER <seq> : Clicked on "<call>" at <freq_khz>"
///
/// Commands sent:
///   SKIMMER/LO_FREQ <hz>   — update LO frequency when panadapter moves
/// </summary>
public sealed class CwSkimmerTelnetClient : ICwSkimmerTelnetClient
{
    private TcpClient?              _tcp;
    private NetworkStream?          _stream;
    private StreamWriter?           _writer;
    private CancellationTokenSource? _readCts;
    private Task?                   _readTask;
    private readonly SemaphoreSlim  _writeLock = new(1, 1);

    public bool IsConnected => _tcp?.Connected ?? false;

    public event Action<double>? FrequencyClicked;

    public async Task ConnectAsync(string host, int port, string callsign, string password,
                                   CancellationToken ct = default)
    {
        _tcp    = new TcpClient();
        await _tcp.ConnectAsync(host, port, ct);
        _stream = _tcp.GetStream();

        await PerformLoginAsync(_stream, callsign, password, ct);

        _writer = new StreamWriter(_stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine   = "\r\n",
        };

        _readCts  = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readTask = Task.Run(() => ReadLoopAsync(_stream, _readCts.Token));
    }

    public async Task SendLoFreqAsync(long freqHz, CancellationToken ct = default)
    {
        if (_writer is null || !IsConnected) return;

        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync($"SKIMMER/LO_FREQ {freqHz}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendQsyAsync(double freqKhz, CancellationToken ct = default)
    {
        if (_writer is null || !IsConnected) return;

        await _writeLock.WaitAsync(ct);
        try
        {
            // CW Skimmer expects frequency in kHz with up to 3 decimal places
            await _writer.WriteLineAsync(
                $"SKIMMER/QSY {freqKhz.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        _readCts?.Cancel();
        if (_readTask is not null)
        {
            try { await _readTask.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        }

        _writer?.Dispose();
        _stream?.Dispose();
        _tcp?.Dispose();

        _writer   = null;
        _stream   = null;
        _tcp      = null;
        _readCts  = null;
        _readTask = null;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();

    // ── Login ────────────────────────────────────────────────────────────────

    private static async Task PerformLoginAsync(
        NetworkStream stream, string callsign, string password, CancellationToken ct)
    {
        var buf  = new byte[4096];
        var text = new StringBuilder();

        using var loginCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loginCts.CancelAfter(TimeSpan.FromSeconds(15));

        bool sentCallsign = false;

        try
        {
            while (true)
            {
                int n = await stream.ReadAsync(buf.AsMemory(), loginCts.Token);
                if (n == 0) break;

                text.Append(StripIac(buf.AsSpan(0, n)));
                var s = text.ToString();

                if (!sentCallsign &&
                    s.Contains("callsign", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteLineToStream(stream, callsign, ct);
                    sentCallsign = true;
                    text.Clear();
                }
                else if (sentCallsign &&
                         s.Contains("password", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteLineToStream(stream, password, ct);
                    break; // Login complete
                }
                else if (sentCallsign)
                {
                    // No password prompt — login may not require one; wait briefly
                    loginCts.CancelAfter(TimeSpan.FromSeconds(2));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout after credentials sent — assume login complete
        }
    }

    private static Task WriteLineToStream(NetworkStream stream, string text, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(text + "\r\n");
        return stream.WriteAsync(bytes.AsMemory(), ct).AsTask();
    }

    // ── Receive loop ──────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                ProcessLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private void ProcessLine(string line)
    {
        var freq = ParseClickedOn(line);
        if (freq.HasValue)
            FrequencyClicked?.Invoke(freq.Value);
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses CW Skimmer's click notification:
    ///   "To ALL de SKIMMER &lt;seq&gt; : Clicked on "&lt;call&gt;" at &lt;freq_khz&gt;"
    /// Returns the frequency in kHz, or null if the line is not a click notification.
    /// </summary>
    public static double? ParseClickedOn(string line)
    {
        if (!line.Contains("Clicked on", StringComparison.OrdinalIgnoreCase))
            return null;

        int atIdx = line.LastIndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (atIdx < 0) return null;

        var freqStr = line.AsSpan(atIdx + 4).Trim();
        if (double.TryParse(freqStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var freq))
            return freq;

        return null;
    }

    // ── IAC stripping ─────────────────────────────────────────────────────────

    private static string StripIac(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0xFF) // Telnet IAC
            {
                if (i + 1 < bytes.Length)
                    i += bytes[i + 1] >= 0xFB ? 2 : 1; // WILL/WONT/DO/DONT + option, or bare cmd
            }
            else if (bytes[i] == '\r' || bytes[i] == '\n' ||
                     (bytes[i] >= 32 && bytes[i] < 128))
            {
                sb.Append((char)bytes[i]);
            }
        }
        return sb.ToString();
    }
}
