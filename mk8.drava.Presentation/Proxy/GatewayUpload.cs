using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Proxy;

public sealed partial class GatewayUpload : IDisposable
{
    private readonly HttpContext _context;
    private readonly IClientStreamWriter<ExchangeFrame> _writer;
    private readonly long _maximumBytes;
    private readonly int _frameBytes;
    private readonly TaskCompletionSource _allowed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stop;
    private readonly BodyDigest _digest = new();
    private readonly ILogger<GatewayUpload> _logger;

    public GatewayUpload(HttpContext context, IClientStreamWriter<ExchangeFrame> writer, long maximumBytes, int frameBytes)
        : this(context, writer, maximumBytes, frameBytes, NullLogger<GatewayUpload>.Instance) { }

    public GatewayUpload(HttpContext context, IClientStreamWriter<ExchangeFrame> writer, long maximumBytes, int frameBytes, ILogger<GatewayUpload> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _context = context;
        _writer = writer;
        _maximumBytes = maximumBytes;
        _frameBytes = frameBytes;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    }

    internal GatewayUpgradeState? Upgrade { get; init; }
    public bool Completed { get; private set; }
    public void Allow() => _allowed.TrySetResult();

    public async Task SendAsync(bool hasBody)
    {
        try
        {
            if (Upgrade is { Requested: true } upgrade)
                await SendBytesAsync(await upgrade.WaitForStreamAsync(_stop.Token).ConfigureAwait(false), boundedBody: false, _stop.Token).ConfigureAwait(false);
            else if (hasBody) await SendBodyAsync(_stop.Token).ConfigureAwait(false);
            _stop.Token.ThrowIfCancellationRequested();
            await _writer.WriteAsync(new ExchangeFrame { Complete = _digest.Complete() }, _context.RequestAborted).ConfigureAwait(false);
            Completed = true;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested && !_context.RequestAborted.IsCancellationRequested) { }
    }

    private async Task SendBodyAsync(CancellationToken cancellationToken)
    {
        await _allowed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var readAhead = new GatewayBodyReadAhead(_context.Request.Body, _frameBytes, cancellationToken);
        await using (readAhead.ConfigureAwait(false))
            await SendBytesAsync(readAhead.Reader, boundedBody: true, cancellationToken).ConfigureAwait(false);
        var feature = _context.Features.Get<IHttpRequestTrailersFeature>();
        if (feature is not { Available: true } || feature.Trailers.Count == 0) return;
        var trailers = new TrailerFrame();
        foreach (var header in feature.Trailers)
            foreach (var value in header.Value) trailers.Headers.Add(new Header { Name = header.Key, Value = value ?? "" });
        FrameLimits.ValidateHeaders(trailers.Headers, trailers: true);
        await _writer.WriteAsync(new ExchangeFrame { Trailers = trailers }, _context.RequestAborted).ConfigureAwait(false);
    }

    private async Task SendBytesAsync(Stream source, bool boundedBody, CancellationToken cancellationToken)
    {
        var buffer = new byte[_frameBytes];
        var observe = _logger.IsEnabled(LogLevel.Debug);
        long readTicks = 0, writeTicks = 0, frames = 0, smallFrames = 0;
        var maximumFrame = 0;
        var endOfStream = false;
        try
        {
            while (true)
            {
                var started = observe ? Stopwatch.GetTimestamp() : 0;
                int count;
                try { count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); }
                finally { if (observe) readTicks += Stopwatch.GetTimestamp() - started; }
                if (count == 0) { endOfStream = true; return; }
                _digest.Append(buffer.AsSpan(0, count));
                if (boundedBody && _digest.Bytes > (ulong)_maximumBytes) throw new BadHttpRequestException("Gateway request body limit exceeded.", StatusCodes.Status413PayloadTooLarge);
                started = observe ? Stopwatch.GetTimestamp() : 0;
                try { await _writer.WriteAsync(new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFrom(buffer, 0, count) } }, cancellationToken).ConfigureAwait(false); }
                finally { if (observe) writeTicks += Stopwatch.GetTimestamp() - started; }
                if (observe)
                {
                    frames++;
                    if (count < 16 * 1024) smallFrames++;
                    maximumFrame = Math.Max(maximumFrame, count);
                }
            }
        }
        finally
        {
            if (observe)
            {
                LogUploadCounters(_logger, _context.Request.IsHttps, _digest.Bytes, frames, smallFrames, maximumFrame, endOfStream);
                LogUploadTiming(_logger, 1000d * readTicks / Stopwatch.Frequency, 1000d * writeTicks / Stopwatch.Frequency);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The task is the sender started by this exchange; stopping must await it before acknowledging to serialize request frames. Kestrel has no SynchronizationContext, and ConfigureAwait(false) avoids context capture.")]
    public async Task StopAsync(Task upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        await _stop.CancelAsync().ConfigureAwait(false);
        await upload.ConfigureAwait(false);
        if (!Completed) await _writer.WriteAsync(new ExchangeFrame { UploadStopped = new UploadStopped() }, _context.RequestAborted).ConfigureAwait(false);
    }

    public void Dispose() { _stop.Dispose(); _digest.Dispose(); }
}
