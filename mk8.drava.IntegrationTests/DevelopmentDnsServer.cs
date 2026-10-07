using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Mk8.Drava.IntegrationTests;

// A real DNS responder owned by the development test. It never changes the machine resolver or hosts file.
internal sealed class DevelopmentDnsServer : IAsyncDisposable
{
    private readonly UdpClient _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private readonly Func<IPAddress?> _answer;
    private readonly Func<IReadOnlyList<string>>? _txtAnswer;
    public bool DropReplies { get; init; }
    public bool TruncateReplies { get; init; }
    public TaskCompletionSource QueryReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Port => ((IPEndPoint)(_listener.Client.LocalEndPoint ?? throw new InvalidOperationException("DNS fixture is unbound."))).Port;

    public DevelopmentDnsServer(IPAddress answer) : this(() => answer) { }
    public DevelopmentDnsServer(Func<IPAddress?> answer) { ArgumentNullException.ThrowIfNull(answer); _answer = answer; _listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); _run = RunAsync(); }

    public DevelopmentDnsServer(Func<IReadOnlyList<string>> txtAnswer, int port = 0)
    {
        ArgumentNullException.ThrowIfNull(txtAnswer); _answer = static () => null; _txtAnswer = txtAnswer;
        _listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, port)); _run = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _listener.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                QueryReceived.TrySetResult();
                if (DropReplies) continue;
                var response = Respond(received.Buffer);
                if (TruncateReplies) response[2] |= 2;
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
        var address = _answer()?.GetAddressBytes() ?? [];
        if (type == 16 && _txtAnswer is not null) return RespondTxt(query, end, _txtAnswer());
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

    internal static byte[] RespondTxt(byte[] query, int questionEnd, IReadOnlyList<string> values)
    {
        if (values.Count > 128) throw new InvalidDataException("Fixture TXT set exceeds its bound.");
        var size = questionEnd;
        foreach (var value in values)
        {
            if (value.Length > 255 || value.Any(static c => !char.IsAscii(c))) throw new InvalidDataException("Fixture TXT character string exceeds its bound.");
            size += 13 + value.Length;
        }
        if (size > 4096) throw new InvalidDataException("Fixture TXT response exceeds its wire bound.");
        var response = new byte[size]; query.AsSpan(0, questionEnd).CopyTo(response);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0x8180);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(6), checked((ushort)values.Count));
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10), 0);
        var offset = questionEnd;
        foreach (var value in values)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset), 0xc00c);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset + 2), 16);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset + 4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(offset + 6), 60);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset + 10), checked((ushort)(value.Length + 1)));
            response[offset + 12] = checked((byte)value.Length);
            System.Text.Encoding.ASCII.GetBytes(value, response.AsSpan(offset + 13, value.Length));
            offset += 13 + value.Length;
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
