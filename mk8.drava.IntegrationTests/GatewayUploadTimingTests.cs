using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayUploadTimingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservationPreservesBodyAndCompletionWithoutRecordingSensitiveFieldsAsync(bool enabled)
    {
        var body = new MemoryStream(new byte[1280], writable: false);
        await using var bodyLifetime = body.ConfigureAwait(true);
        var context = CreateContext(body);
        var logger = new TimingLogger(enabled);
        using var writer = new DigestWriter();
        using var upload = new GatewayUpload(context, writer, 2048, 512, logger);
        upload.Allow();
        await upload.SendAsync(hasBody: true).ConfigureAwait(true);
        Assert.True(upload.Completed);
        Assert.True(writer.Complete);
        Assert.Equal(1280UL, writer.Bytes);
        if (!enabled) { Assert.Empty(logger.Records); return; }
        Assert.Equal(2, logger.Records.Count);
        var record = logger.Records[0];
        Assert.Equal(1280UL, record["ReadBytes"]);
        Assert.Equal(3L, record["WrittenFrames"]);
        Assert.Equal(512, record["MaximumFrameBytes"]);
        Assert.Equal(true, record["EndOfStream"]);
        Assert.DoesNotContain("synthetic-private", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", logger.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectedBytesAreNotReportedAsWrittenFramesOrSuccessfulCompletionAsync()
    {
        var body = new MemoryStream(new byte[1280], writable: false);
        await using var bodyLifetime = body.ConfigureAwait(true);
        var context = CreateContext(body);
        var logger = new TimingLogger(enabled: true);
        using var writer = new DigestWriter();
        using var upload = new GatewayUpload(context, writer, 1024, 512, logger);
        upload.Allow();
        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(() => upload.SendAsync(hasBody: true)).ConfigureAwait(true);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
        Assert.False(upload.Completed);
        Assert.False(writer.Complete);
        Assert.Equal(1024UL, writer.Bytes);
        Assert.Equal(2, logger.Records.Count);
        var record = logger.Records[0];
        Assert.Equal(1280UL, record["ReadBytes"]);
        Assert.Equal(2L, record["WrittenFrames"]);
        Assert.Equal(false, record["EndOfStream"]);
    }

    private static DefaultHttpContext CreateContext(Stream body)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = body;
        context.Request.Scheme = "https";
        context.Request.Path = "/synthetic-private";
        context.Request.QueryString = new QueryString("?sig=synthetic-private");
        context.Request.Headers.Authorization = "Bearer synthetic-private";
        return context;
    }

    private sealed class DigestWriter : IClientStreamWriter<ExchangeFrame>, IDisposable
    {
        private readonly BodyDigest _digest = new();
        public WriteOptions? WriteOptions { get; set; }
        public ulong Bytes => _digest.Bytes;
        public bool Complete { get; private set; }
        public Task WriteAsync(ExchangeFrame message) => WriteAsync(message, CancellationToken.None);
        public Task WriteAsync(ExchangeFrame message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (message.FrameCase == ExchangeFrame.FrameOneofCase.Data)
            {
                _digest.Append((message.Data ?? throw new InvalidDataException("Missing data frame.")).Payload.Span);
            }
            else { Assert.Equal(ExchangeFrame.FrameOneofCase.Complete, message.FrameCase); _digest.Verify(message.Complete ?? throw new InvalidDataException("Missing completion frame.")); Complete = true; }
            return Task.CompletedTask;
        }
        public Task CompleteAsync() => Task.CompletedTask;
        public void Dispose() => _digest.Dispose();
    }

    private sealed class TimingLogger(bool enabled) : ILogger<GatewayUpload>
    {
        public List<IReadOnlyDictionary<string, object?>> Records { get; } = [];
        public string Message { get; private set; } = "";
        public bool IsEnabled(LogLevel logLevel) => enabled && logLevel == LogLevel.Debug;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Debug, logLevel);
            var fields = Assert.IsType<IEnumerable<KeyValuePair<string, object?>>>(state, exactMatch: false);
            Records.Add(fields.ToDictionary(StringComparer.Ordinal));
            Message += formatter(state, exception) + "\n";
        }
    }
}
