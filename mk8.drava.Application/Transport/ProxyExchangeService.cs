using Grpc.Core;
using Mk8.Drava.Application.BLL.Proxy;
using Mk8.Drava.Application.INF.Proxy.Exchange;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;

namespace Mk8.Drava.Application.Transport;

internal sealed class ProxyExchangeService(ProxyRequestPipeline pipeline, ProxyForwarder forwarder, UpgradeForwarder upgrades,
    ApplicationBootstrap bootstrap, ExchangeAdmission admission) : ProxyExchange.ProxyExchangeBase
{
    public override async Task Exchange(IAsyncStreamReader<ExchangeFrame> requestStream, IServerStreamWriter<ExchangeFrame> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);
        if (!await admission.Slots.WaitAsync(0, context.CancellationToken).ConfigureAwait(false))
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Application exchange admission exhausted."));
        try { await ExecuteAsync(requestStream, responseStream, context.CancellationToken).ConfigureAwait(false); }
        catch (InvalidDataException)
        {
            await responseStream.WriteAsync(new ExchangeFrame { Reset = new Reset { Code = "invalid_exchange", SafeReason = "Exchange contract validation failed." } }, context.CancellationToken).ConfigureAwait(false);
        }
        finally { admission.Slots.Release(); }
    }

    private async Task ExecuteAsync(IAsyncStreamReader<ExchangeFrame> requestStream, IServerStreamWriter<ExchangeFrame> responseStream, CancellationToken cancellationToken)
    {
        using var headDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headDeadline.CancelAfter(TimeSpan.FromSeconds(3));
        if (!await requestStream.MoveNext(headDeadline.Token).ConfigureAwait(false) || requestStream.Current.FrameCase != ExchangeFrame.FrameOneofCase.Request)
            throw new InvalidDataException("The exchange must start with a request head.");
        headDeadline.CancelAfter(Timeout.InfiniteTimeSpan);
        var head = requestStream.Current.Request ?? throw new InvalidDataException("Request head is missing.");
        FrameLimits.ValidateRequest(head);
        if (!string.Equals(head.GatewayId, bootstrap.GatewayId, StringComparison.Ordinal) || head.GatewayGeneration != 1)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Presentation identity or generation is not authorized."));
        if (head.WantsUpgrade && (!string.Equals(head.ClientProtocol, "HTTP/1.1", StringComparison.Ordinal) || head.HasBody))
            throw new InvalidDataException("Upgrades require a bodyless HTTP/1.1 request.");
        await RunExchangeAsync(head, requestStream, responseStream, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunExchangeAsync(RequestHead head, IAsyncStreamReader<ExchangeFrame> requestStream, IServerStreamWriter<ExchangeFrame> responseStream, CancellationToken cancellationToken)
    {
        using var exchangeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = exchangeCancellation.Token;
        var window = head.StreamWindowFrames == 0 ? 4 : (int)head.StreamWindowFrames;
        using var writer = new ExchangeServerWriter(responseStream, window, token);
        var inbound = new ExchangeInbound(requestStream, writer, window);
        using var stream = new ExchangeClientStream(inbound, writer, head, inbound.RequestConsumedAsync);
        async Task ExecutePipelineAsync()
        {
            if (!head.HasBody && !head.WantsUpgrade) await stream.VerifyEmptyUploadAsync(token).ConfigureAwait(false);
            var executor = new MdravaProxyExchangeExecutor(stream, forwarder, upgrades);
            await pipeline.ExecuteAsync(ExchangeRequestMapper.ToRequest(head), executor, token).ConfigureAwait(false);
            inbound.AllowClientCompletion();
            await stream.CompleteAsync(token).ConfigureAwait(false);
        }
        var pump = inbound.PumpAsync(token);
        var execute = ExecutePipelineAsync();
        try
        {
            var first = await Task.WhenAny(pump, execute).ConfigureAwait(false);
            if (first == pump) await pump.ConfigureAwait(false);
            await execute.ConfigureAwait(false);
            using var closing = CancellationTokenSource.CreateLinkedTokenSource(token);
            closing.CancelAfter(TimeSpan.FromSeconds(5));
            await pump.WaitAsync(closing.Token).ConfigureAwait(false);
        }
        finally
        {
            await exchangeCancellation.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(pump, execute).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or RpcException or System.Threading.Channels.ChannelClosedException) { }
        }
    }
}
