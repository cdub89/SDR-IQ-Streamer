using System;
using System.Threading;
using System.Threading.Tasks;

namespace SDRIQStreamer.App;

internal sealed class ThrottledStatusEmitter : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _minInterval;
    private readonly Action<Action> _postToUi;
    private readonly Action<string> _emitStatus;

    private DateTime _lastEmitUtc;
    private CancellationTokenSource? _flushCts;
    private string? _pendingMessage;

    public ThrottledStatusEmitter(TimeSpan minInterval, Action<Action> postToUi, Action<string> emitStatus)
    {
        _minInterval = minInterval;
        _postToUi = postToUi;
        _emitStatus = emitStatus;
    }

    public void Enqueue(string message)
    {
        var now = DateTime.UtcNow;
        CancellationTokenSource? toCancel = null;
        CancellationTokenSource? nextCts = null;
        TimeSpan delay = TimeSpan.Zero;
        var emitNow = false;

        lock (_gate)
        {
            var elapsed = now - _lastEmitUtc;
            if (elapsed >= _minInterval)
            {
                _lastEmitUtc = now;
                _pendingMessage = null;
                emitNow = true;
            }
            else
            {
                _pendingMessage = message;
                delay = _minInterval - elapsed;
                toCancel = _flushCts;
                nextCts = new CancellationTokenSource();
                _flushCts = nextCts;
            }
        }

        if (emitNow)
        {
            _emitStatus(message);
            return;
        }

        if (toCancel is not null)
        {
            try { toCancel.Cancel(); } catch { }
            toCancel.Dispose();
        }

        if (nextCts is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, nextCts.Token);
                _postToUi(FlushPending);
            }
            catch (OperationCanceledException) { }
        });
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pendingMessage = null;
            _lastEmitUtc = DateTime.MinValue;

            if (_flushCts is not null)
            {
                try { _flushCts.Cancel(); } catch { }
                _flushCts.Dispose();
                _flushCts = null;
            }
        }
    }

    private void FlushPending()
    {
        string? message;
        lock (_gate)
        {
            message = _pendingMessage;
            _pendingMessage = null;
            _lastEmitUtc = DateTime.UtcNow;

            _flushCts?.Dispose();
            _flushCts = null;
        }

        if (!string.IsNullOrWhiteSpace(message))
            _emitStatus(message);
    }

    public void Dispose() => Clear();
}
