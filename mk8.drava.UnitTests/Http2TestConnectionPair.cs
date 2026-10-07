using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Http2;

namespace Mk8.Drava.UnitTests;

internal sealed class Http2TestConnectionPair : IDisposable
{
    private readonly TcpClient _client;
    private readonly TcpClient _server;
    public CancellationTokenSource Deadline { get; } = new(TimeSpan.FromSeconds(10));
    public NetworkStream Client => _client.GetStream();
    public NetworkStream Server => _server.GetStream();

    private Http2TestConnectionPair(TcpClient client, TcpClient server) { _client = client; _server = server; }

    public static async Task<Http2TestConnectionPair> CreateAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        var client = new TcpClient();
        TcpClient? server = null;
        try
        {
            listener.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token).ConfigureAwait(false);
            server = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(false);
            return new Http2TestConnectionPair(client, server);
        }
        catch { server?.Dispose(); client.Dispose(); throw; }
        finally { listener.Stop(); }
    }

    public async Task InitializeAsync(Http2UpstreamConnection connection, uint initialWindow = 65535)
    {
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(2));
        async Task RespondAsync()
        {
            await Http2TestFrames.ReadExactAsync(Server, 24, Deadline.Token).ConfigureAwait(false);
            await Http2TestFrames.ReadAsync(Server, Deadline.Token).ConfigureAwait(false);
            var settings = new byte[6];
            BinaryPrimitives.WriteUInt16BigEndian(settings, 4);
            BinaryPrimitives.WriteUInt32BigEndian(settings.AsSpan(2), initialWindow);
            await Http2TestFrames.WriteAsync(Server, Http2TestFrameType.Settings, 0, 0, settings, Deadline.Token).ConfigureAwait(false);
            await Http2TestFrames.ReadAsync(Server, Deadline.Token).ConfigureAwait(false);
        }
        var response = RespondAsync();
        try { await connection.InitializeAsync(timeouts, Deadline.Token).ConfigureAwait(false); await response.ConfigureAwait(false); }
        catch
        {
            await Deadline.CancelAsync().ConfigureAwait(false);
            try { await response.ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
            throw;
        }
    }

    public Task WindowAsync(int streamId, uint increment)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, increment);
        return Http2TestFrames.WriteAsync(Server, Http2TestFrameType.WindowUpdate, 0, streamId, payload, Deadline.Token);
    }

    public async Task RequestAsync(Http2UpstreamConnection connection, bool hasBody = false)
    {
        await connection.SendHeadersAsync([new ProxyHeaderField(":method", hasBody ? "POST" : "GET"),
            new(":scheme", "https"), new(":authority", "peer.test"), new(":path", "/")], !hasBody,
            RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5)), Deadline.Token).ConfigureAwait(false);
        await Http2TestFrames.ReadAsync(Server, Deadline.Token).ConfigureAwait(false);
    }

    public void Dispose() { Deadline.Dispose(); _server.Dispose(); _client.Dispose(); }
}
