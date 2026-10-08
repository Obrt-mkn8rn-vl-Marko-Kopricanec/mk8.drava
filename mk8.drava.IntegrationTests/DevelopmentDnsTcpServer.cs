using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentDnsTcpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private readonly IReadOnlyList<string>? _values;
    private readonly IPAddress? _address;
    public bool BlockResponse { get; init; }
    public bool InvalidFrame { get; init; }
    public TaskCompletionSource QueryReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DevelopmentDnsTcpServer(int port, IReadOnlyList<string> values)
    {
        _values = values; _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start(); _run = RunAsync();
    }

    public DevelopmentDnsTcpServer(int port, IPAddress address)
    {
        _address = address; _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start(); _run = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamLifetime = stream.ConfigureAwait(false);
                var prefix = new byte[2]; await stream.ReadExactlyAsync(prefix, _stop.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
                if (length is < 17 or > 512) throw new InvalidDataException("Development DNS query exceeds its frame bound.");
                var query = new byte[length]; await stream.ReadExactlyAsync(query, _stop.Token).ConfigureAwait(false);
                QueryReceived.TrySetResult();
                if (BlockResponse) { await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token).ConfigureAwait(false); return; }
                var response = _values is null ? DevelopmentDnsServer.RespondAddress(query, query.Length, _address) : DevelopmentDnsServer.RespondTxt(query, query.Length, _values);
                BinaryPrimitives.WriteUInt16BigEndian(prefix, InvalidFrame ? (ushort)1 : checked((ushort)response.Length));
                await stream.WriteAsync(prefix, _stop.Token).ConfigureAwait(false);
                if (!InvalidFrame) await stream.WriteAsync(response, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (IOException) when (_stop.IsCancellationRequested) { }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "This fixture owns the TCP query task, cancels its actual accept/read/write operations and joins it before disposing listener and cancellation source.")]
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false); _listener.Stop();
        await _run.ConfigureAwait(false); _listener.Dispose(); _stop.Dispose();
    }
}
