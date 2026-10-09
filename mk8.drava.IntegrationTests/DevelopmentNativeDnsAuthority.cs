using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Mk8.Drava.Application.INF.Dns;
using Mk8.Drava.Application.INF.Dns.Management;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentNativeDnsAuthority : IAsyncDisposable
{
    private readonly TcpListener _tcp = new(IPAddress.Loopback, 0);
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _udpTask;
    private readonly Task _tcpTask;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string, ushort), ReadOnlyMemory<byte>[]> _records = [];
    private uint _serial = 1;
    public int Port { get; }
    public bool Truncate { get; init; }
    public bool Authoritative { get; set; } = true;
    public int TcpQueries { get; private set; }
    public uint Serial { get { lock (_gate) return _serial; } set { lock (_gate) _serial = value; } }

    public DevelopmentNativeDnsAuthority()
    {
        _tcp.Start(); Port = ((IPEndPoint)_tcp.LocalEndpoint).Port;
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
            _udpTask = RunUdpAsync(); _tcpTask = RunTcpAsync();
        }
        catch { _tcp.Dispose(); _stop.Dispose(); throw; }
    }
    public void Set(string owner, ushort type, params ReadOnlyMemory<byte>[] values) { lock (_gate) _records[(owner, type)] = values.Select(static value => (ReadOnlyMemory<byte>)value.ToArray()).ToArray(); }

    private async Task RunUdpAsync()
    {
        try
        {
            while (true)
            {
                var request = await _udp.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                var response = Reply(request.Buffer);
                if (Truncate) response[2] |= 2;
                await _udp.SendAsync(response, request.RemoteEndPoint, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task RunTcpAsync()
    {
        try
        {
            while (true)
            {
                using var client = await _tcp.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                using var stream = client.GetStream();
                var prefix = new byte[2]; await stream.ReadExactlyAsync(prefix, _stop.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
                if (length is < 12 or > 512) throw new InvalidDataException("Native DNS fixture query exceeds its bound.");
                var query = new byte[length]; await stream.ReadExactlyAsync(query, _stop.Token).ConfigureAwait(false);
                var response = Reply(query); TcpQueries++;
                BinaryPrimitives.WriteUInt16BigEndian(prefix, checked((ushort)response.Length));
                await stream.WriteAsync(prefix, _stop.Token).ConfigureAwait(false); await stream.WriteAsync(response, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private byte[] Reply(byte[] query)
    {
        var position = 12; var owner = DnsWire.ReadName(query, ref position); var type = DnsWire.Read16(query, ref position);
        if (DnsWire.Read16(query, ref position) != 1 || position != query.Length || (query[2] & 1) != 0)
            throw new InvalidDataException("Native observation must use one direct nonrecursive question.");
        ReadOnlyMemory<byte>[] values; uint serial;
        lock (_gate) { values = _records.GetValueOrDefault((owner, type)) ?? []; serial = _serial; }
        var soa = Soa(serial);
        var answers = type == 6 && string.Equals(owner, "site.test", StringComparison.Ordinal) ? new ReadOnlyMemory<byte>[] { soa } : values;
        var bytes = new List<byte>(query);
        var negative = answers.Length == 0;
        foreach (var value in answers) AppendRecord(bytes, owner, type, value.Span);
        if (negative) AppendRecord(bytes, "site.test", 6, soa);
        var packet = bytes.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), checked((ushort)(Authoritative ? 0x8400 : 0x8000)));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), checked((ushort)answers.Length));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(8), (ushort)(negative ? 1 : 0));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10), 0); return packet;
    }

    private static byte[] Soa(uint serial)
    {
        var bytes = new List<byte>(NativeDnsSession.WireName("ns.site.test")); bytes.AddRange(NativeDnsSession.WireName("hostmaster.site.test"));
        var data = new byte[20]; BinaryPrimitives.WriteUInt32BigEndian(data, serial); bytes.AddRange(data); return bytes.ToArray();
    }
    private static void AppendRecord(List<byte> bytes, string owner, ushort type, ReadOnlySpan<byte> value)
    {
        bytes.AddRange(NativeDnsSession.WireName(owner));
        var header = new byte[10]; BinaryPrimitives.WriteUInt16BigEndian(header, type); BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 300); BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), checked((ushort)value.Length));
        bytes.AddRange(header); bytes.AddRange(value.ToArray());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "This development fixture owns both DNS loops, cancels and joins those exact tasks, then disposes both sockets and its cancellation source.")]
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(_udpTask, _tcpTask).ConfigureAwait(false); }
        finally { _udp.Dispose(); _tcp.Dispose(); _stop.Dispose(); }
    }
}
