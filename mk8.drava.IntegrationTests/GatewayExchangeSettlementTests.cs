using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayExchangeSettlementTests
{
    [Fact]
    public async Task FullPublicBodyDoesNotCompleteThePrivateExchangeAsync()
    {
        using var scenario = new SettlementScenario(extraFrame: false);
        var receive = scenario.ReceiveAsync();
        try
        {
            await scenario.Reader.CompletionRead.Task.WaitAsync(scenario.Token).ConfigureAwait(true);
            await scenario.Sink.CreditWritten.Task.WaitAsync(scenario.Token).ConfigureAwait(true);
            Assert.Equal(404, scenario.Context.Response.StatusCode);
            Assert.Equal(9L, scenario.Context.Response.ContentLength);
            Assert.Equal("Not Found"u8.ToArray(), scenario.Body.ToArray());
            Assert.False(receive.IsCompleted);
            Assert.Equal(0, scenario.Sink.Completions);
            Assert.Equal(1U, scenario.Sink.ResponseCredits);

            scenario.Reader.ReleaseCompletion.TrySetResult();
            await scenario.Reader.EofRead.Task.WaitAsync(scenario.Token).ConfigureAwait(true);
            await scenario.Sink.Completed.Task.WaitAsync(scenario.Token).ConfigureAwait(true);
            Assert.Equal(1, scenario.Sink.Completions);
            Assert.False(receive.IsCompleted);

            scenario.Reader.ReleaseEof.TrySetResult();
            await receive.ConfigureAwait(true);
        }
        finally
        {
            scenario.Reader.ReleaseCompletion.TrySetResult();
            scenario.Reader.ReleaseEof.TrySetResult();
            await receive.ConfigureAwait(true);
        }
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The assertion joins the receive task started and owned by this test. Its finally releases the EOF gate and joins the same task before disposing the scenario.")]
    public async Task AFrameAfterVerifiedCompletionStillFailsTheExchangeAsync()
    {
        using var scenario = new SettlementScenario(extraFrame: true);
        scenario.Reader.ReleaseCompletion.TrySetResult();
        var receive = scenario.ReceiveAsync();
        try
        {
            await scenario.Sink.Completed.Task.WaitAsync(scenario.Token).ConfigureAwait(true);
            scenario.Reader.ReleaseEof.TrySetResult();
            await Assert.ThrowsAsync<InvalidDataException>(() => receive).ConfigureAwait(true);
        }
        finally
        {
            scenario.Reader.ReleaseEof.TrySetResult();
            try { await receive.ConfigureAwait(true); }
            catch (InvalidDataException) { }
        }
        Assert.Equal("Not Found"u8.ToArray(), scenario.Body.ToArray());
        Assert.Equal(1, scenario.Sink.Completions);
    }

    private sealed class SettlementScenario : IDisposable
    {
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(10));
        private readonly ExchangeClientWriter _writer;
        private readonly GatewayUpload _upload;
        private readonly GatewayResponse _response;
        public MemoryStream Body { get; } = new();
        public DefaultHttpContext Context { get; } = new();
        public SettlementReader Reader { get; }
        public SettlementWriter Sink { get; } = new();
        public CancellationToken Token => _deadline.Token;

        public SettlementScenario(bool extraFrame)
        {
            Context.RequestAborted = Token;
            Context.Response.Body = Body;
            Reader = new SettlementReader(extraFrame);
            _writer = new ExchangeClientWriter(Sink, 4, Token);
            _upload = new GatewayUpload(Context, _writer, 1024, 1024);
            _response = new GatewayResponse(Context, Reader, _writer, _upload, static () => Task.CompletedTask);
        }

        public Task ReceiveAsync() => _response.ReceiveAsync();
        public void Dispose()
        {
            _upload.Dispose();
            _writer.Dispose();
            Body.Dispose();
            _deadline.Dispose();
        }
    }

    private sealed class SettlementReader(bool extraFrame) : IAsyncStreamReader<ExchangeFrame>
    {
        private int _next;
        public ExchangeFrame Current { get; private set; } = new();
        public TaskCompletionSource CompletionRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EofRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseEof { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            switch (_next++)
            {
                case 0:
                    var head = new ResponseHead { StatusCode = 404 };
                    head.Headers.Add(new Header { Name = "Content-Length", Value = "9" });
                    Current = new ExchangeFrame { Response = head };
                    return true;
                case 1:
                    Current = new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFrom("Not Found"u8) } };
                    return true;
                case 2:
                    CompletionRead.TrySetResult();
                    await ReleaseCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    using (var digest = new BodyDigest())
                    {
                        digest.Append("Not Found"u8);
                        Current = new ExchangeFrame { Complete = digest.Complete() };
                    }
                    return true;
                default:
                    EofRead.TrySetResult();
                    await ReleaseEof.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (extraFrame)
                    {
                        Current = new ExchangeFrame { Reset = new Reset() };
                        return true;
                    }
                    return false;
            }
        }
    }

    private sealed class SettlementWriter : IClientStreamWriter<ExchangeFrame>
    {
        public WriteOptions? WriteOptions { get; set; }
        public int Completions { get; private set; }
        public uint ResponseCredits { get; private set; }
        public TaskCompletionSource CreditWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WriteAsync(ExchangeFrame message)
        {
            RecordCredit(message);
            return Task.CompletedTask;
        }
        private void RecordCredit(ExchangeFrame message)
        {
            Assert.Equal(ExchangeFrame.FrameOneofCase.Consumed, message.FrameCase);
            var consumed = message.Consumed ?? throw new InvalidDataException("Expected response consumption credit.");
            Assert.Equal(Consumed.Types.Direction.Response, consumed.Direction);
            ResponseCredits += consumed.Frames;
            CreditWritten.TrySetResult();
        }
        public Task WriteAsync(ExchangeFrame message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordCredit(message);
            return Task.CompletedTask;
        }
        public Task CompleteAsync() { Completions++; Completed.TrySetResult(); return Task.CompletedTask; }
    }
}
