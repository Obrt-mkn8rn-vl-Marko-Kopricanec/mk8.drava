using System.Runtime.ExceptionServices;

namespace Mk8.Drava.Application.INF.Proxy.Http2;

internal sealed class Http2UpstreamFlowControl
{
    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = CreateSignal();
    private long _connectionWindow = 65535;
    private long _streamWindow = 65535;
    private uint _initialWindow = 65535;
    private int _maximumFrameBytes = 16384;
    private ExceptionDispatchInfo? _failure;

    public async ValueTask<int> ReserveAsync(int requested, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requested, 1);
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                _failure?.Throw();
                var count = (int)Math.Min(Math.Min(requested, _maximumFrameBytes), Math.Min(_connectionWindow, _streamWindow));
                if (count > 0)
                {
                    _connectionWindow -= count;
                    _streamWindow -= count;
                    return count;
                }
                changed = _changed.Task;
            }
            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void AddCredit(bool connection, uint increment)
    {
        if (increment is 0 or > int.MaxValue) throw new Http2UpstreamProtocolException("Invalid HTTP/2 window increment.");
        lock (_gate)
        {
            _failure?.Throw();
            var window = connection ? _connectionWindow : _streamWindow;
            if (window + increment > int.MaxValue) throw new Http2UpstreamProtocolException("HTTP/2 window exceeds its bound.");
            if (connection) _connectionWindow += increment;
            else _streamWindow += increment;
            Signal();
        }
    }

    public void SetInitialWindow(uint size)
    {
        if (size > int.MaxValue) throw new Http2UpstreamProtocolException("Invalid HTTP/2 initial stream window.");
        lock (_gate)
        {
            _failure?.Throw();
            var window = _streamWindow + size - _initialWindow;
            if (window > int.MaxValue) throw new Http2UpstreamProtocolException("HTTP/2 stream window exceeds its bound.");
            _streamWindow = window;
            _initialWindow = size;
            Signal();
        }
    }

    public void SetMaximumFrameBytes(uint size)
    {
        if (size is < 16384 or > 16777215) throw new Http2UpstreamProtocolException("Invalid HTTP/2 peer frame size.");
        lock (_gate) _maximumFrameBytes = (int)size;
    }

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            _failure ??= ExceptionDispatchInfo.Capture(exception);
            Signal();
        }
    }

    private void Signal()
    {
        var previous = _changed;
        _changed = CreateSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource CreateSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
