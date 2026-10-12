using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Primitives;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;

namespace Mk8.Drava.Presentation.Proxy;

public sealed class GatewayResponse(HttpContext context, IAsyncStreamReader<ExchangeFrame> reader, ExchangeClientWriter writer, GatewayUpload upload, Func<Task> stopUpload)
{
    private readonly GatewayResponseInbound _inbound = new(reader, writer);
    private CancellationToken _receiveCancellation;
    internal GatewayUpgradeState? Upgrade { get; init; }
    private bool _finalHead;
    private bool _complete;
    private bool _uploadAllowed;
    private bool _uploadStopped;
    private int _informationalCount;
    private TrailerFrame? _trailers;

    public async Task ReceiveAsync()
    {
        using var digest = new BodyDigest();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        await ReceiveOwnedAsync(digest, stop).ConfigureAwait(false);
    }

    private async Task ReceiveOwnedAsync(BodyDigest digest, CancellationTokenSource stop)
    {
        _receiveCancellation = stop.Token;
        var consume = _inbound.ConsumeAsync(frame => FrameAsync(frame, digest), stop.Token);
        var pump = _inbound.PumpAsync(stop.Token);
        try
        {
            var first = await Task.WhenAny(pump, consume).ConfigureAwait(false);
            await first.ConfigureAwait(false);
            await Task.WhenAll(pump, consume).ConfigureAwait(false);
            if (!_complete) throw new InvalidDataException("Application ended without response completion.");
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(pump, consume).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or RpcException or OperationCanceledException or InvalidOperationException) { }
        }
    }

    private async Task FrameAsync(ExchangeFrame frame, BodyDigest digest)
    {
        switch (frame.FrameCase)
        {
            case ExchangeFrame.FrameOneofCase.UploadAllowed:
                if (_finalHead || _uploadAllowed || _uploadStopped) throw new InvalidDataException("Unexpected upload allowance.");
                _uploadAllowed = true;
                upload.Allow();
                break;
            case ExchangeFrame.FrameOneofCase.StopUpload:
                if (_uploadStopped) throw new InvalidDataException("Duplicate upload stop.");
                _uploadStopped = true;
                await stopUpload().ConfigureAwait(false);
                break;
            case ExchangeFrame.FrameOneofCase.Response:
                await HeadAsync(frame.Response ?? throw new InvalidDataException("Response head is missing.")).ConfigureAwait(false);
                break;
            case ExchangeFrame.FrameOneofCase.Data:
                await DataAsync(frame.Data ?? throw new InvalidDataException("Response data is missing."), digest).ConfigureAwait(false);
                break;
            case ExchangeFrame.FrameOneofCase.Trailers:
                if (!_finalHead || _trailers is not null || Upgrade?.AcceptedStream is not null) throw new InvalidDataException("Unexpected response trailers.");
                var trailers = frame.Trailers ?? throw new InvalidDataException("Response trailers are missing.");
                FrameLimits.ValidateHeaders(trailers.Headers, trailers: true);
                _trailers = trailers;
                break;
            case ExchangeFrame.FrameOneofCase.Complete:
                if (!_finalHead) throw new InvalidDataException("Response completion has no final head.");
                digest.Verify(frame.Complete ?? throw new InvalidDataException("Response completion is missing."));
                ApplyTrailers(context, _trailers);
                _complete = true;
                await writer.CompleteAsync().ConfigureAwait(false);
                break;
            default:
                throw new InvalidDataException("Application reset or invalid response frame.");
        }
    }

    private async Task HeadAsync(ResponseHead head)
    {
        FrameLimits.ValidateResponse(head);
        if (_finalHead) throw new InvalidDataException("Multiple final response heads.");
        if (head.Informational)
        {
            if (++_informationalCount > 8) throw new InvalidDataException("Informational response count exceeded.");
            // Kestrel sends 100 when the admitted body is first read.
            if (head.StatusCode == 100) return;
            var feature = context.Features.Get<IGatewayInformationalResponseFeature>()
                ?? throw new InvalidDataException("Client transport has no informational response adapter.");
            var streamId = context.Features.Get<IHttp2StreamIdFeature>()?.StreamId ?? 0;
            await feature.WriteAsync(head, streamId, _receiveCancellation).ConfigureAwait(false);
            return;
        }
        _finalHead = true;
        if (head.Upgrade)
        {
            var upgrade = Upgrade ?? throw new InvalidDataException("Upgrade presentation was not negotiated.");
            upgrade.Validate(context, head);
            ApplyHead(context, head);
            await upgrade.AcceptAsync(context).ConfigureAwait(false);
        }
        else
        {
            ApplyHead(context, head);
            await context.Response.StartAsync(_receiveCancellation).ConfigureAwait(false);
        }
    }

    private async Task DataAsync(DataFrame data, BodyDigest digest)
    {
        if (!_finalHead || _trailers is not null) throw new InvalidDataException("Data outside response body.");
        FrameLimits.ValidateData(data);
        digest.Append(data.Payload.Span);
        var destination = Upgrade?.AcceptedStream ?? context.Response.Body;
        await destination.WriteAsync(data.Payload.Memory, _receiveCancellation).ConfigureAwait(false);
        await destination.FlushAsync(_receiveCancellation).ConfigureAwait(false);
        _inbound.ResponseConsumed();
        await writer.WriteAsync(new ExchangeFrame { Consumed = new Consumed { Direction = Consumed.Types.Direction.Response, Frames = 1 } }, _receiveCancellation).ConfigureAwait(false);
    }

    private static void ApplyHead(HttpContext context, ResponseHead head)
    {
        context.Response.StatusCode = checked((int)head.StatusCode);
        foreach (var header in head.Headers)
        {
            if (string.Equals(header.Name, "trailer", StringComparison.OrdinalIgnoreCase))
            {
                if (context.Response.SupportsTrailers())
                    foreach (var name in header.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) context.Response.DeclareTrailer(name);
                continue;
            }
            context.Response.Headers.Append(header.Name, new StringValues(header.Value));
        }
    }

    private static void ApplyTrailers(HttpContext context, TrailerFrame? trailers)
    {
        if (trailers is null || trailers.Headers.Count == 0) return;
        if (!context.Response.SupportsTrailers()) throw new InvalidDataException("Client transport did not negotiate response trailers.");
        foreach (var header in trailers.Headers) context.Response.AppendTrailer(header.Name, header.Value);
    }
}
