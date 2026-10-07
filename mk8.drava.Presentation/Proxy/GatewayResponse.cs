using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;

namespace Mk8.Drava.Presentation.Proxy;

public sealed class GatewayResponse(HttpContext context, IAsyncStreamReader<ExchangeFrame> reader, ExchangeClientWriter writer, GatewayUpload upload, Func<Task> stopUpload)
{
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
        while (await reader.MoveNext(context.RequestAborted).ConfigureAwait(false))
        {
            if (_complete) throw new InvalidDataException("Frames follow response completion.");
            await FrameAsync(reader.Current, digest).ConfigureAwait(false);
        }
        if (!_complete) throw new InvalidDataException("Application ended without response completion.");
    }

    private async Task FrameAsync(ExchangeFrame frame, BodyDigest digest)
    {
        switch (frame.FrameCase)
        {
            case ExchangeFrame.FrameOneofCase.Consumed:
                var consumed = frame.Consumed ?? throw new InvalidDataException("Consumption frame is missing.");
                if (consumed.Direction != Consumed.Types.Direction.Request) throw new InvalidDataException("Invalid credit direction.");
                writer.RequestWindow.Return(consumed.Frames);
                break;
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
                ApplyTrailers(_trailers);
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
            // Kestrel supplies its own 100 on the admitted body read. Its public API cannot emit other 1xx yet.
            if (head.StatusCode != 100 || ++_informationalCount > 8) throw new InvalidDataException("Public informational response capability is unavailable.");
            return;
        }
        _finalHead = true;
        if (head.Upgrade)
        {
            var upgrade = Upgrade ?? throw new InvalidDataException("Upgrade presentation was not negotiated.");
            upgrade.Validate(context, head);
            ApplyHead(head);
            await upgrade.AcceptAsync(context).ConfigureAwait(false);
        }
        else
        {
            ApplyHead(head);
            await context.Response.StartAsync(context.RequestAborted).ConfigureAwait(false);
        }
    }

    private async Task DataAsync(DataFrame data, BodyDigest digest)
    {
        if (!_finalHead || _trailers is not null) throw new InvalidDataException("Data outside response body.");
        FrameLimits.ValidateData(data);
        digest.Append(data.Payload.Span);
        var destination = Upgrade?.AcceptedStream ?? context.Response.Body;
        await destination.WriteAsync(data.Payload.Memory, context.RequestAborted).ConfigureAwait(false);
        await destination.FlushAsync(context.RequestAborted).ConfigureAwait(false);
        await writer.WriteAsync(new ExchangeFrame { Consumed = new Consumed { Direction = Consumed.Types.Direction.Response, Frames = 1 } }, context.RequestAborted).ConfigureAwait(false);
    }

    private void ApplyHead(ResponseHead head)
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

    private void ApplyTrailers(TrailerFrame? trailers)
    {
        if (trailers is null || trailers.Headers.Count == 0) return;
        if (!context.Response.SupportsTrailers()) throw new InvalidDataException("Client transport did not negotiate response trailers.");
        foreach (var header in trailers.Headers) context.Response.AppendTrailer(header.Name, header.Value);
    }
}
