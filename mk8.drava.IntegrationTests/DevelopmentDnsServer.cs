using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Mk8.Drava.IntegrationTests;

// A real DNS responder owned by the development test. It never changes the machine resolver or hosts file.
internal sealed class DevelopmentDnsServer : IAsyncDisposable
{
    private readonly UdpClient _listener = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private readonly IPAddress _answer;
    public int Port => ((IPEndPoint)(_listener.Client.LocalEndPoint ?? throw new InvalidOperationException("DNS fixture is unbound."))).Port;

    public DevelopmentDnsServer(IPAddress answer) { _answer = answer; _run = RunAsync(); }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _listener.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                var response = Respond(received.Buffer);
                await _listener.SendAsync(response, received.RemoteEndPoint, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private byte[] Respond(byte[] query)
    {
        if (query.Length is < 17 or > 4096) throw new InvalidDataException("Fixture received invalid DNS query.");
        var end = 12;
        while (query[end] != 0) end += 1 + query[end];
        end++;
        var type = BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(end));
        end += 4;
        var address = _answer.GetAddressBytes();
        var answer = type == 1 && address.Length == 4;
        var response = new byte[end + (answer ? 16 : 0)];
        query.AsSpan(0, end).CopyTo(response);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0x8180);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(6), (ushort)(answer ? 1 : 0));
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10), 0);
        if (answer)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(end), 0xc00c);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(end + 2), 1);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(end + 4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(end + 6), 60);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(end + 10), 4);
            address.CopyTo(response, end + 12);
        }
        return response;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "This fixture owns the DNS receive loop, starts it in its constructor, cancels it and joins that exact task before disposing its socket and token source.")]
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _run.ConfigureAwait(false);
        _listener.Dispose();
        _stop.Dispose();
    }
}
