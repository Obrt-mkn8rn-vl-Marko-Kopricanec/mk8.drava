using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class PrivateHttp2SettingsProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualPrivateListenerAdvertisesConfiguredReceiveLimitsAsync(bool configured)
    {
        var settings = configured ? new PrivateHttp2Settings { InitialStreamWindowBytes = 262144, InitialConnectionWindowBytes = 1048576, MaximumStreamsPerConnection = 16 } : new PrivateHttp2Settings();
        var proxy = await TwoProcessProxy.StartAsync(1, manualRoute: false, privateHttp2: settings).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        await AssertWireAsync(proxy.Ipc, settings).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualNodeAgentPrivateListenerAdvertisesConfiguredReceiveLimitsAsync(bool configured)
    {
        var settings = configured ? new PrivateHttp2Settings { InitialStreamWindowBytes = 262144, InitialConnectionWindowBytes = 1048576, MaximumStreamsPerConnection = 16 } : new PrivateHttp2Settings();
        var proxy = await TwoProcessProxy.StartAsync(1, enrolledSite: true, manualRoute: false, relayNode: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var agent = await DevelopmentNodeAgent.StartAsync(proxy, privateHttp2: settings).ConfigureAwait(true);
        await using var agentLifetime = agent.ConfigureAwait(true);
        await AssertWireAsync(agent.Descriptor.LocalEndpoint, settings).ConfigureAwait(true);
    }

    private static async Task AssertWireAsync(IpcEndpoint endpoint, PrivateHttp2Settings settings)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = await OpenAsync(endpoint, stop.Token).ConfigureAwait(true);
        await using var streamLifetime = stream.ConfigureAwait(true);
        await stream.WriteAsync("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray(), stop.Token).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(stream, Http2TestFrameType.Settings, 0, 0, ReadOnlyMemory<byte>.Empty, stop.Token).ConfigureAwait(true);
        var receivedSettings = false;
        var receivedConnectionCredit = false;
        for (var index = 0; index < 8 && (!receivedSettings || !receivedConnectionCredit); index++)
        {
            var frame = await Http2TestFrames.ReadAsync(stream, stop.Token).ConfigureAwait(true);
            if (frame.Type == Http2TestFrameType.Settings && frame.Flags == 0)
            {
                Assert.Equal(0, frame.StreamId);
                var values = ReadSettings(frame.Payload.Span);
                Assert.Equal((uint)settings.InitialStreamWindowBytes, values[4]);
                Assert.Equal((uint)settings.MaximumStreamsPerConnection, values[3]);
                receivedSettings = true;
            }
            else if (frame.Type == Http2TestFrameType.WindowUpdate && frame.StreamId == 0)
            {
                Assert.Equal(settings.InitialConnectionWindowBytes - 65535, BinaryPrimitives.ReadInt32BigEndian(frame.Payload.Span));
                receivedConnectionCredit = true;
            }
        }
        Assert.True(receivedSettings);
        Assert.True(receivedConnectionCredit);
    }

    private static Dictionary<ushort, uint> ReadSettings(ReadOnlySpan<byte> payload)
    {
        Assert.Equal(0, payload.Length % 6);
        Dictionary<ushort, uint> values = [];
        for (var offset = 0; offset < payload.Length; offset += 6)
            values.Add(BinaryPrimitives.ReadUInt16BigEndian(payload[offset..]), BinaryPrimitives.ReadUInt32BigEndian(payload[(offset + 2)..]));
        return values;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Connected socket ownership transfers to the returned NetworkStream with ownsSocket=true. The catch disposes on connect or construction failure; the calling test asynchronously disposes the returned stream. Named-pipe ownership is transferred and cleaned up by the same contract.")]
    private static async Task<Stream> OpenAsync(IpcEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (endpoint.UnixSocketPath.Length != 0)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.UnixSocketPath), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        }
        var pipe = new NamedPipeClientStream(".", endpoint.NamedPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false); return pipe; }
        catch { await pipe.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
