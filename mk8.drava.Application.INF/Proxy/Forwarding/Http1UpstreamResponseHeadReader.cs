using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.INF.Proxy.Http1;
using System.Buffers;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
internal static class Http1UpstreamResponseHeadReader
{
    public static async ValueTask<Http1HeadReadResult> ReadAsync(Stream upstreamStream, int maxResponseHeadBytes, TimeSpan responseHeadTimeout, ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(maxResponseHeadBytes);
        var totalBytesRead = 0;
        try
        {
            while (totalBytesRead < maxResponseHeadBytes)
            {
                var bytesRead = await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await upstreamStream.ReadAsync(buffer.AsMemory(totalBytesRead, maxResponseHeadBytes - totalBytesRead), timeoutToken).ConfigureAwait(false), responseHeadTimeout, ProxyTimeoutKind.UpstreamResponseHead, cancellationToken).ConfigureAwait(false);
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
}
