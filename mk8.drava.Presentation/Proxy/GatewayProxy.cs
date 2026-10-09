using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;

namespace Mk8.Drava.Presentation.Proxy;

public sealed class GatewayProxy : IDisposable
{
    private readonly ApplicationChannel _channel;
    private readonly GatewayBootstrap _bootstrap;
    private readonly SemaphoreSlim _admission;

    public GatewayProxy(ApplicationChannel channel, GatewayBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(bootstrap);
        _channel = channel;
        _bootstrap = bootstrap;
        _admission = new SemaphoreSlim(bootstrap.MaxConcurrentExchanges, bootstrap.MaxConcurrentExchanges);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!await _admission.WaitAsync(0, context.RequestAborted).ConfigureAwait(false)) { context.Response.StatusCode = 503; return; }
        try { await ExchangeAsync(context).ConfigureAwait(false); }
        catch (Exception exception) when (exception is RpcException or IOException or InvalidDataException or InvalidOperationException or OperationCanceledException)
        {
            if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested) context.Abort();
            else context.Response.StatusCode = exception is BadHttpRequestException badRequest ? badRequest.StatusCode : exception is InvalidDataException ? 502 : 503;
        }
        finally { _admission.Release(); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "This method starts and joins both exchange tasks. The callback passes its own sender task to ordered stop acknowledgment; Kestrel has no SynchronizationContext and all production awaits avoid capture.")]
    private async Task ExchangeAsync(HttpContext context)
    {
        var maximumBodyBytes = _bootstrap.ResolveRequestBodyLimit(context.Request.Host.Host);
        if (context.Request.ContentLength > maximumBodyBytes) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
        var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is not null)
        {
            if (bodyLimit.IsReadOnly) throw new InvalidOperationException("Gateway body limit must be selected before reading the request.");
            bodyLimit.MaxRequestBodySize = maximumBodyBytes;
        }
        var head = GatewayRequestMapper.ToHead(context, _bootstrap, generation: 1);
        if (head.WantsUpgrade && (!string.Equals(head.ClientProtocol, "HTTP/1.1", StringComparison.Ordinal) || head.HasBody)) { context.Response.StatusCode = 400; return; }
        var upgrade = head.WantsUpgrade ? new GatewayUpgradeState(requested: true) : GatewayUpgradeState.None;
        await using var upgradeLifetime = upgrade.ConfigureAwait(false);
        var client = new ProxyExchange.ProxyExchangeClient(_channel.Invoker);
        using var call = client.Exchange(_channel.Credentials, cancellationToken: context.RequestAborted);
        using var writer = new ExchangeClientWriter(call.RequestStream, _bootstrap.StreamWindowFrames, context.RequestAborted);
        await writer.WriteAsync(new ExchangeFrame { Request = head }, context.RequestAborted).ConfigureAwait(false);
        using var upload = new GatewayUpload(context, writer, maximumBodyBytes, _bootstrap.FrameBytes) { Upgrade = upgrade };
        var send = upload.SendAsync(head.HasBody);
        var receive = new GatewayResponse(context, call.ResponseStream, writer, upload, () => upload.StopAsync(send)) { Upgrade = upgrade }.ReceiveAsync();
        try
        {
            var first = await Task.WhenAny(send, receive).ConfigureAwait(false);
            if (first == send) await send.ConfigureAwait(false);
            await receive.ConfigureAwait(false);
            await send.ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
        }
        catch
        {
            call.Dispose();
            try { await upload.StopAsync(send).ConfigureAwait(false); }
            catch (Exception exception) when (exception is RpcException or IOException or InvalidDataException or OperationCanceledException or InvalidOperationException) { }
            try { await Task.WhenAll(send, receive).ConfigureAwait(false); }
            catch (Exception exception) when (exception is RpcException or IOException or InvalidDataException or OperationCanceledException or InvalidOperationException) { }
            throw;
        }
    }

    public void Dispose() => _admission.Dispose();
}
