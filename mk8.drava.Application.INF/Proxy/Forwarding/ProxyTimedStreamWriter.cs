using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
internal static class ProxyTimedStreamWriter
{
    public static async ValueTask WriteAsync(Stream destination, ReadOnlyMemory<byte> bytes, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ProxyTimeoutPolicy.RunAsync(async timeoutToken =>
        {
            await destination.WriteAsync(bytes, timeoutToken).ConfigureAwait(false);
        }, timeout, ProxyTimeoutKind.DownstreamWrite, cancellationToken).ConfigureAwait(false);
    }
}
