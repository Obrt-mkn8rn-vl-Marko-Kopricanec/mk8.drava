using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
internal sealed class Http1BodyReader(Stream stream, ReadOnlyMemory<byte> initialBytes, ProxyMetrics metrics, TimeSpan readTimeout, ProxyTimeoutKind timeoutKind)
{
    private readonly Stream _stream = stream;
    private readonly ProxyMetrics _metrics = metrics;
    private readonly TimeSpan _readTimeout = readTimeout;
    private readonly ProxyTimeoutKind _timeoutKind = timeoutKind;
    private ReadOnlyMemory<byte> _initialBytes = initialBytes;

    public bool HasBufferedBytes => !_initialBytes.IsEmpty;

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
        var one = new byte[1];
        while (bytes.Count < maxLineBytes)
        {
            if (await ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
                throw new IOException("Source closed before the required bytes were read.");
            var current = one[0];
            bytes.Add(current);
            if (previous == (byte)'\r' && current == (byte)'\n')
            {
                return [.. bytes];
            }

            previous = current;
        }

        throw new IOException("HTTP line exceeded the configured maximum length.");
    }
}
