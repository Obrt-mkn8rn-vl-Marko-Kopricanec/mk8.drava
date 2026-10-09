using System.Buffers;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Connections;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayInformationalOutputLifetimeTests
{
    [Fact]
    public async Task CloseCancelsQueuedWritersAndJoinsTheBlockedOutputPumpAsync()
    {
        var input = new Pipe();
        var destination = new Pipe(new PipeOptions(pauseWriterThreshold: 16, resumeWriterThreshold: 8));
        using var disconnect = new CancellationTokenSource();
        using var request = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connection = new DefaultConnectionContext { Transport = new GatewayResponseDuplexPipe(input.Reader, destination.Writer), ConnectionClosed = disconnect.Token };
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var output = new GatewayResponseOutput(connection, http2: false);
        await using var outputLifetime = output.ConfigureAwait(true);
        List<Task> writers = [];
        var head = new ResponseHead { StatusCode = 103, Informational = true };
        head.Headers.Add(new Header { Name = "link", Value = new string('x', 30000) });
        try
        {
            for (var index = 0; index < 16; index++) writers.Add(output.WriteAsync(head, 0, request.Token));
            var held = await destination.Reader.ReadAsync(deadline.Token).ConfigureAwait(true);
            Assert.True(held.Buffer.Length > 16);
            Assert.All(writers, static writer => Assert.False(writer.IsCompleted));
            var closing = output.DisposeAsync().AsTask();
            Assert.Same(closing, output.DisposeAsync().AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Task.WhenAll(writers).WaitAsync(deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
            Assert.All(writers, static writer => Assert.True(writer.IsCanceled));
            Assert.False(closing.IsCompleted);
            await disconnect.CancelAsync().ConfigureAwait(true);
            await closing.WaitAsync(deadline.Token).ConfigureAwait(true);
            Assert.All(writers, static writer => Assert.True(writer.IsCompleted));
            destination.Reader.AdvanceTo(held.Buffer.End);
        }
        finally
        {
            await request.CancelAsync().ConfigureAwait(true);
            await disconnect.CancelAsync().ConfigureAwait(true);
            try { await Task.WhenAll(writers).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            await output.DisposeAsync().ConfigureAwait(true);
            await CompletePipesAsync(input, destination).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task EarlyResponseWaitsForTheWholeFragmentedHttp2HeaderBlockAsync()
    {
        var input = new Pipe();
        var destination = new Pipe();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connection = new DefaultConnectionContext { Transport = new GatewayResponseDuplexPipe(input.Reader, destination.Writer), ConnectionClosed = deadline.Token };
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var output = new GatewayResponseOutput(connection, http2: true);
        await using var outputLifetime = output.ConfigureAwait(true);
        // SETTINGS, followed by a HEADERS frame with END_HEADERS unset and a split frame header.
        byte[] prefix = [0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 1, 1];
        byte[] middle = [0, 0, 0, 0, 1, 0x88];
        byte[] suffix = [0, 0, 0, 9, 4, 0, 0, 0, 1];
        Task? early = null;
        try
        {
            await output.Writer.WriteAsync(prefix, deadline.Token).ConfigureAwait(true);
            var first = await destination.Reader.ReadAsync(deadline.Token).ConfigureAwait(true);
            Assert.Equal(prefix.Length, first.Buffer.Length);
            destination.Reader.AdvanceTo(first.Buffer.End);
            early = output.WriteAsync(new ResponseHead { StatusCode = 103, Informational = true }, 3, deadline.Token);
            Assert.False(early.IsCompleted);
            await output.Writer.WriteAsync(middle, deadline.Token).ConfigureAwait(true);
            var second = await destination.Reader.ReadAsync(deadline.Token).ConfigureAwait(true);
            Assert.Equal(middle.Length, second.Buffer.Length);
            Assert.False(early.IsCompleted);
            destination.Reader.AdvanceTo(second.Buffer.End);
            await output.Writer.WriteAsync(suffix, deadline.Token).ConfigureAwait(true);
            await early.WaitAsync(deadline.Token).ConfigureAwait(true);
            var last = await destination.Reader.ReadAsync(deadline.Token).ConfigureAwait(true);
            var actual = last.Buffer.ToArray();
            Assert.Equal(suffix, actual[..suffix.Length]);
            Assert.Equal(1, actual[suffix.Length + 3]);
            Assert.Equal(4, actual[suffix.Length + 4]);
            Assert.Equal(3, actual[suffix.Length + 8]);
            destination.Reader.AdvanceTo(last.Buffer.End);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            if (early is not null)
            {
                try { await early.ConfigureAwait(true); }
                catch (OperationCanceledException) { }
            }
            await output.DisposeAsync().ConfigureAwait(true);
            await CompletePipesAsync(input, destination).ConfigureAwait(true);
        }
    }

    private static async Task CompletePipesAsync(Pipe input, Pipe destination)
    {
        await input.Writer.CompleteAsync().ConfigureAwait(false);
        await input.Reader.CompleteAsync().ConfigureAwait(false);
        await destination.Writer.CompleteAsync().ConfigureAwait(false);
        await destination.Reader.CompleteAsync().ConfigureAwait(false);
    }
}
