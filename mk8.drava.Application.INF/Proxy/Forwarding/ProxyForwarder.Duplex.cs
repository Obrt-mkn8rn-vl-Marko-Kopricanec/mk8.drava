using System.Net.Sockets;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.INF.Proxy.Exchange;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class ProxyForwarder
{
    private static async ValueTask<ResponseForwardingResult> ForwardFramedDuplexAsync(ExchangeClientStream exchange, string method,
        Func<CancellationToken, Task> uploadBody, Func<CancellationToken, Action<int>?, Task<ResponseForwardingResult>> readResponse,
        CancellationToken cancellationToken)
    {
        using var uploadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var responseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopped = 0;
        async Task UploadAsync()
        {
            try { await uploadBody(uploadCancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (Volatile.Read(ref stopped) != 0 && !cancellationToken.IsCancellationRequested) { }
        }
        void StopForHead(int status)
        {
            if (status < 300 && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) return;
            Interlocked.Exchange(ref stopped, 1);
            uploadCancellation.Cancel();
        }
        var upload = UploadAsync();
        var response = readResponse(responseCancellation.Token, StopForHead);
        try
        {
            if (await Task.WhenAny(upload, response).ConfigureAwait(false) == upload) await upload.ConfigureAwait(false);
            var result = await response.ConfigureAwait(false);
            Interlocked.Exchange(ref stopped, 1);
            await uploadCancellation.CancelAsync().ConfigureAwait(false);
            await upload.ConfigureAwait(false);
            await exchange.StopUploadAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await uploadCancellation.CancelAsync().ConfigureAwait(false);
            await responseCancellation.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(upload, response).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ProxyTimeoutException or Grpc.Core.RpcException) { }
            throw;
        }
    }
}
