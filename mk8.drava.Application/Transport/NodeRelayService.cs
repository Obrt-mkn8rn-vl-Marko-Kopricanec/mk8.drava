using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Grpc.Core;
using Mk8.Drava.Application.INF.NodeRelay;
using Mk8.Drava.Application.BLL.NodeRelay;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.Application.Transport;

internal sealed class NodeRelayService(NodeRelayAuthorizer authorizer, SemaphoreSlim admission, RelayConnectionPolicy policy) : Mk8.Drava.Transport.Protocol.V1.NodeRelay.NodeRelayBase
{
    public override async Task Relay(IAsyncStreamReader<RelayFrame> requestStream, IServerStreamWriter<RelayFrame> responseStream, ServerCallContext context)
    {
        var http = context.GetHttpContext();
        if (!http.Request.IsHttps) throw new RpcException(new Status(StatusCode.PermissionDenied, "Relay requires controller mutual TLS."));
        if (!await admission.WaitAsync(0, context.CancellationToken).ConfigureAwait(false)) throw new RpcException(new Status(StatusCode.ResourceExhausted, "Node relay admission exhausted."));
        try { await ExecuteAsync(requestStream, responseStream, context).ConfigureAwait(false); }
        catch (UnauthorizedAccessException) { throw new RpcException(new Status(StatusCode.PermissionDenied, "Relay capability is not authorized.")); }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid relay contract.")); }
        finally { admission.Release(); }
    }

    private async Task ExecuteAsync(IAsyncStreamReader<RelayFrame> requests, IServerStreamWriter<RelayFrame> responses, ServerCallContext context)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(policy.OpeningTimeoutSeconds));
        if (!await requests.MoveNext(deadline.Token).ConfigureAwait(false) || requests.Current.FrameCase != RelayFrame.FrameOneofCase.Open)
            throw new InvalidDataException("Relay must start with a capability.");
        var open = requests.Current.Open ?? throw new InvalidDataException("Relay capability is missing.");
        if (open.Version != 1 || open.StreamWindowFrames is < 1 or > 8) throw new InvalidDataException("Invalid relay version or window.");
        var peer = await context.GetHttpContext().Connection.GetClientCertificateAsync(deadline.Token).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Controller peer is missing.");
        var (capability, endpoint) = authorizer.Authorize(peer, open.CapabilityJson.Memory, open.Signature.Span);
        if (!string.Equals(open.SiteId, capability.Identity.SiteId, StringComparison.Ordinal) || !string.Equals(open.NodeId, capability.Identity.NodeId, StringComparison.Ordinal) ||
            !string.Equals(open.InstanceId, capability.Identity.InstanceId, StringComparison.Ordinal) || !string.Equals(open.BootId, capability.Identity.BootId, StringComparison.Ordinal) ||
            !string.Equals(open.ExchangeId, capability.ExchangeId, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Relay envelope differs from its signed capability.");
        using var socket = new Socket(IPAddress.Parse(endpoint.Address).AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(endpoint.Address), endpoint.Port), deadline.Token).ConfigureAwait(false);
        var maximumDuration = Math.Min(capability.MaximumDurationSeconds, policy.MaximumDurationSeconds);
        var maximumBytes = Math.Min(capability.MaximumBytesPerDirection, policy.MaximumBytesPerDirection);
        var window = Math.Min((int)open.StreamWindowFrames, policy.StreamWindowFrames);
        deadline.CancelAfter(TimeSpan.FromSeconds(maximumDuration));
        using var backend = new NetworkStream(socket, ownsSocket: false);
        using var writer = new RelayFrameWriter((frame, token) => responses.WriteAsync(frame, token), window);
        await writer.WriteAsync(new RelayFrame { Accepted = new RelayAccepted { Version = 1, CapabilityId = capability.CapabilityId, StreamWindowFrames = (uint)window,
            MaximumBytesPerDirection = (ulong)maximumBytes, MaximumDurationSeconds = (uint)maximumDuration } }, deadline.Token).ConfigureAwait(false);
        var relay = RelayDuplexStream.Create(requests, writer, Consumed.Types.Direction.Response, maximumBytes, deadline.Cancel, deadline.Token);
        await using var relayLifetime = relay.ConfigureAwait(false);
        await RunDirectionsAsync(relay, backend, socket, deadline).ConfigureAwait(false);
    }

    private static async Task RunDirectionsAsync(RelayDuplexStream relay, NetworkStream backend, Socket socket, CancellationTokenSource lifetime)
    {
        async Task UploadAsync()
        {
            await relay.CopyToAsync(backend, FrameLimits.MaximumFrameBytes, lifetime.Token).ConfigureAwait(false);
            socket.Shutdown(SocketShutdown.Send);
        }
        async Task DownloadAsync()
        {
            await backend.CopyToAsync(relay, FrameLimits.MaximumFrameBytes, lifetime.Token).ConfigureAwait(false);
            await relay.CompleteWritesAsync(lifetime.Token).ConfigureAwait(false);
        }
        var upload = UploadAsync();
        var download = DownloadAsync();
        var pump = relay.WaitForCompletionAsync(lifetime.Token);
        try
        {
            var first = await Task.WhenAny(upload, download, pump).ConfigureAwait(false);
            await first.ConfigureAwait(false);
            await Task.WhenAll(upload, download).ConfigureAwait(false);
            lifetime.CancelAfter(TimeSpan.FromSeconds(5));
            await relay.FinishAsync(lifetime.Token).ConfigureAwait(false);
            await pump.ConfigureAwait(false);
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(upload, download, pump).ConfigureAwait(false); }
            catch (Exception exception) when (exception is InvalidDataException or OperationCanceledException or IOException or RpcException) { }
        }
    }
}
