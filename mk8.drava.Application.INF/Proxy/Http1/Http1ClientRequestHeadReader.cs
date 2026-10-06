using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;

namespace Mk8.Drava.Application.INF.Proxy.Http1;
internal static class Http1ClientRequestHeadReader
{
    public static ValueTask<Http1HeadReadResult> ReadAsync(Stream clientStream, byte[] requestHeadBuffer, int maxRequestHeadBytes, TimeSpan timeout, ProxyTimeoutKind timeoutKind, ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        return ProxyTimeoutPolicy.RunAsync(timeoutToken => ReadWithoutTimeoutAsync(clientStream, requestHeadBuffer, maxRequestHeadBytes, metrics, timeoutToken), timeout, timeoutKind, cancellationToken);
    }

    private static async ValueTask<Http1HeadReadResult> ReadWithoutTimeoutAsync(Stream clientStream, byte[] requestHeadBuffer, int maxRequestHeadBytes, ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        var totalBytesRead = 0;
        while (totalBytesRead < maxRequestHeadBytes)
        {
            var bytesRead = await clientStream.ReadAsync(requestHeadBuffer.AsMemory(totalBytesRead, 1), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return totalBytesRead == 0 ? Http1HeadReadResult.RequestEmpty() : Http1HeadReadResult.RequestIncomplete(totalBytesRead);
            }

            totalBytesRead += bytesRead;
            metrics.AddBytesRead(bytesRead);
            var requestHeadLength = Http1HeadTerminator.FindLength(requestHeadBuffer.AsSpan(0, totalBytesRead));
            if (requestHeadLength > 0)
            {
                return Http1HeadReadResult.Read(requestHeadLength, totalBytesRead, requestHeadBuffer.AsMemory(0, requestHeadLength), ReadOnlyMemory<byte>.Empty);
            }
        }

        return Http1HeadReadResult.RequestTooLarge(totalBytesRead);
    }
}
