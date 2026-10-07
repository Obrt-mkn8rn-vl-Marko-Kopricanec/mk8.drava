using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentHttp1Peer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private int _connections;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int Connections => Volatile.Read(ref _connections);

    public DevelopmentHttp1Peer() => _listener.Start();

    public async Task RespondTwiceAsync(string firstResponse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firstResponse);
        var requests = 0;
        while (requests < 2)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _connections);
            var stream = client.GetStream();
            while (requests < 2 && await ReadHeadAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                var response = requests == 0 ? firstResponse : "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nsafe";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                requests++;
            }
        }
    }

    private static async Task<bool> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var one = new byte[1];
        var ending = 0u;
        for (var count = 0; count < 32768; count++)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0) return false;
            ending = (ending << 8) | one[0];
            if (ending == 0x0d0a0d0a) return true;
        }
        throw new InvalidDataException("Development HTTP/1 peer received an oversized request head.");
    }

    public void Dispose() => _listener.Dispose();
}
