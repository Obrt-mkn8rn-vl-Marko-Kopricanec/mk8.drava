using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
internal sealed class Http1BodyReader
{
    private readonly Stream _stream;
    private readonly ProxyMetrics _metrics;
    private readonly TimeSpan _readTimeout;
    private readonly ProxyTimeoutKind _timeoutKind;
    private ReadOnlyMemory<byte> _initialBytes;
    public Http1BodyReader(Stream stream, ReadOnlyMemory<byte> initialBytes, ProxyMetrics metrics, TimeSpan readTimeout, ProxyTimeoutKind timeoutKind)
    {
        _stream = stream;
        _initialBytes = initialBytes;
        _metrics = metrics;
        _readTimeout = readTimeout;
        _timeoutKind = timeoutKind;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        if (_initialBytes.Length > 0)
        {
            var bytesToCopy = Math.Min(destination.Length, _initialBytes.Length);
            _initialBytes[..bytesToCopy].CopyTo(destination);
            _initialBytes = _initialBytes[bytesToCopy..];
            return bytesToCopy;
        }

        var bytesRead = await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await _stream.ReadAsync(destination, timeoutToken).ConfigureAwait(false), _readTimeout, _timeoutKind, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesRead(bytesRead);
        return bytesRead;
    }

    public async ValueTask<byte[]> ReadExactAsync(int length, CancellationToken cancellationToken)
    {
        var bytes = new byte[length];
        var total = 0;
        while (total < length)
        {
            var bytesRead = await ReadAsync(bytes.AsMemory(total, length - total), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new IOException("Source closed before the required bytes were read.");
            }

            total += bytesRead;
        }

        return bytes;
    }

    public async ValueTask<byte[]> ReadLineWithCrlfAsync(int maxLineBytes, CancellationToken cancellationToken)
    {
        List<byte> bytes = [];
        var previous = (byte)0;
        while (bytes.Count < maxLineBytes)
        {
            var one = await ReadExactAsync(1, cancellationToken).ConfigureAwait(false);
            var current = one[0];
            bytes.Add(current);
            if (previous == (byte)'\r' && current == (byte)'\n')
            {
                return bytes.ToArray();
            }

            previous = current;
        }

        throw new IOException("HTTP line exceeded the configured maximum length.");
    }
}
