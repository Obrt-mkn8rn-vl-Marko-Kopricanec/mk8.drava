using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.INF.Proxy.Http1;
using System.Buffers;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
internal static class Http1UpstreamResponseHeadReader
{
    public static async ValueTask<Http1HeadReadResult> ReadAsync(Stream upstreamStream, int maxResponseHeadBytes, TimeSpan responseHeadTimeout, ProxyMetrics metrics, CancellationToken cancellationToken, Task? requestCompleted = null)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(maxResponseHeadBytes);
        var totalBytesRead = 0;
        try
        {
            while (totalBytesRead < maxResponseHeadBytes)
            {
                var destination = buffer.AsMemory(totalBytesRead, maxResponseHeadBytes - totalBytesRead);
                var bytesRead = totalBytesRead == 0 && requestCompleted is { IsCompleted: false }
                    ? await ReadDuringUploadAsync(upstreamStream, destination, requestCompleted, responseHeadTimeout, cancellationToken).ConfigureAwait(false)
                    : await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await upstreamStream.ReadAsync(destination, timeoutToken).ConfigureAwait(false), responseHeadTimeout, ProxyTimeoutKind.UpstreamResponseHead, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return Http1HeadReadResult.ResponseUnreadable(totalBytesRead);
                }

                totalBytesRead += bytesRead;
                metrics.AddBytesRead(bytesRead);
                var headLength = Http1HeadTerminator.FindLength(buffer.AsSpan(0, totalBytesRead));
                if (headLength > 0)
                {
                    var headBytes = buffer.AsMemory(0, headLength).ToArray();
                    var initialBody = buffer.AsMemory(headLength, totalBytesRead - headLength).ToArray();
                    return Http1HeadReadResult.Read(headLength, totalBytesRead, headBytes, initialBody);
                }
            }

            return Http1HeadReadResult.ResponseUnreadable(totalBytesRead);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Mechanically reuse the framed upstream wait: read early replies concurrently,
    // then start the configured head wait when the request has actually been sent.
    private static async ValueTask<int> ReadDuringUploadAsync(Stream upstreamStream, Memory<byte> destination, Task requestCompleted, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var read = upstreamStream.ReadAsync(destination, deadline.Token).AsTask();
        try
        {
            if (await Task.WhenAny(read, requestCompleted).WaitAsync(deadline.Token).ConfigureAwait(false) != read)
                deadline.CancelAfter(timeout);
            return await read.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProxyTimeoutException(ProxyTimeoutKind.UpstreamResponseHead, timeout);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            try { await read.ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException) { }
        }
    }
}
