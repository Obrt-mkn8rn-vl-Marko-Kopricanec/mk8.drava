using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.BLL.NodeRelay;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Contracts.Relay.V1;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.Application.INF.NodeRelay;

public sealed class RegisteredRelayConnector : IAsyncDisposable
{
    private readonly RegistryCoordinator _registry;
    private readonly DestinationAvailabilityStore _availability;
    private readonly string _siteId;
    private readonly string _localNodeId;
    private readonly X509Certificate2 _controller;
    private readonly X509Certificate2 _root;
    private readonly string _epoch;
    private readonly TimeProvider _clock;
    private readonly RelayConnectionPolicy _policy;
    private readonly SemaphoreSlim _admission;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task> _owned = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _quiesced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _opening;
    private bool _disposed;
    private Task? _disposeTask;

    public RegisteredRelayConnector(string siteId, string localNodeId, RegistryCoordinator registry, DestinationAvailabilityStore availability,
        X509Certificate2 controller, X509Certificate2 root, TimeProvider clock, RelayConnectionPolicy policy)
    {
        RegistryNames.RequireLabel(siteId); RegistryNames.RequireLabel(localNodeId);
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(controller); ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(policy);
        _siteId = siteId; _localNodeId = localNodeId; _registry = registry; _availability = availability; _controller = controller; _root = root; _clock = clock;
        _epoch = ControllerCertificateRole.Epoch(controller, siteId);
        ControllerCertificateRole.Validate(controller, root, siteId, _epoch, clock);
        if (!controller.HasPrivateKey) throw new InvalidDataException("Relay controller signing key is missing.");
        _policy = policy;
        _admission = new SemaphoreSlim(policy.MaximumConcurrentConnections, policy.MaximumConcurrentConnections);
    }

    public async ValueTask<Stream?> TryConnectAsync(UpstreamTransportEndpoint endpoint, RelayPurpose purpose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.MembershipPartition.Length == 0) return null;
        if (purpose is not (RelayPurpose.Exchange or RelayPurpose.Readiness)) throw new InvalidDataException("Invalid relay purpose.");
        var intent = RequireCurrent(endpoint, purpose);
        if (intent.Relay is null)
        {
            if (!string.Equals(intent.Identity.NodeId, _localNodeId, StringComparison.Ordinal) && IPAddress.IsLoopback(IPAddress.Parse(intent.Address)))
                throw new IOException("Remote loopback requires its enrolled node relay.");
            return null;
        }
        if (!string.Equals(intent.Identity.NodeId, _localNodeId, StringComparison.Ordinal) && IPAddress.IsLoopback(IPAddress.Parse(intent.Relay.Address)))
            throw new IOException("Remote relay listener cannot be controller-local loopback.");
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _opening++; }
        var admitted = false;
        try
        {
            admitted = await _admission.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (!admitted) throw new IOException("Controller relay admission is exhausted.");
            return await OpenAsync(endpoint, intent, purpose, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception) { if (admitted) _admission.Release(); throw new IOException("Authenticated node relay connection failed.", exception); }
        catch { if (admitted) _admission.Release(); throw; }
        finally { lock (_gate) { _opening--; SignalQuiescence(); } }
    }

    private InstanceIntent RequireCurrent(UpstreamTransportEndpoint endpoint, RelayPurpose purpose)
    {
        if (!_registry.StorageHealthy) throw new IOException("Registry authority is unavailable.");
        var state = _registry.State;
        if (!state.Instances.TryGetValue(endpoint.Name, out var intent) || intent.Draining || state.IsTombstoned(intent.Identity) ||
            !string.Equals(endpoint.MembershipPartition, intent.Identity.Partition, StringComparison.Ordinal) || !string.Equals(endpoint.Address, intent.Address, StringComparison.Ordinal) ||
            endpoint.Port != intent.Port || !string.Equals(endpoint.Scheme, intent.Scheme, StringComparison.Ordinal) || !string.Equals(endpoint.Protocol, intent.Protocol, StringComparison.Ordinal) ||
            !state.Grants.TryGetValue(intent.Identity.NodeId, out var grant) || !grant.Authorizes(intent, _clock.GetUtcNow())) throw new IOException("Relay membership is not current or authorized.");
        var status = _availability.Status(intent.Identity);
        if (!status.LeaseValid || status.Revoked || purpose == RelayPurpose.Exchange && !_availability.IsEligible(intent.Identity)) throw new IOException("Relay membership is not eligible for this purpose.");
        return intent;
    }

    private RelayCapability CreateCapability(UpstreamTransportEndpoint endpoint, InstanceIntent intent, RelayPurpose purpose)
    {
        var now = _clock.GetUtcNow();
        var until = now.AddSeconds(_policy.CapabilityLifetimeSeconds);
        var grantUntil = _registry.State.Grants[intent.Identity.NodeId].NotAfterUtc;
        if (until > grantUntil) until = grantUntil;
        return new RelayCapability
        {
            Identity = new RegistrationIdentity { SiteId = _siteId, NodeId = intent.Identity.NodeId, OwnerId = intent.Identity.OwnerId, ServiceId = intent.Identity.ServiceId,
                ContractId = intent.Identity.ContractId, InstanceId = intent.Identity.InstanceId, BootId = intent.Identity.BootId },
            Advertisement = new ServiceAdvertisement { DeploymentId = intent.DeploymentId, Address = intent.Address, Port = intent.Port, Scheme = intent.Scheme, Protocol = intent.Protocol,
                ReadinessPath = intent.ReadinessPath, Zone = intent.Zone, Weight = intent.Weight, Relay = intent.Relay },
            AgentBootId = intent.Relay!.AgentBootId, ControllerEpoch = _epoch, CapabilityId = Guid.NewGuid().ToString("N"), ExchangeId = Guid.NewGuid().ToString("N"), Purpose = purpose,
            TlsServerName = endpoint.EffectiveSniHost, ValidateBackendCertificate = endpoint.ValidateCertificate,
            IssuedAtUnixMilliseconds = now.ToUnixTimeMilliseconds(), ExpiresAtUnixMilliseconds = until.ToUnixTimeMilliseconds(),
            MaximumBytesPerDirection = purpose == RelayPurpose.Readiness ? Math.Min(1024 * 1024, _policy.MaximumBytesPerDirection) : _policy.MaximumBytesPerDirection,
            MaximumDurationSeconds = purpose == RelayPurpose.Readiness ? Math.Min(5, _policy.MaximumDurationSeconds) : _policy.MaximumDurationSeconds,
        };
    }

    private async ValueTask<Stream> OpenAsync(UpstreamTransportEndpoint endpoint, InstanceIntent intent, RelayPurpose purpose, CancellationToken cancellationToken)
    {
        var capability = CreateCapability(endpoint, intent, purpose);
        var payload = RelayCapabilityJson.Encode(capability);
        using var key = _controller.GetECDsaPrivateKey() ?? throw new InvalidDataException("Controller signing key is missing.");
        var relayEndpoint = intent.Relay ?? throw new InvalidOperationException("Relay locator is missing.");
        CancellationTokenSource? lifetime = null;
        GrpcChannel? channel = null;
        AsyncDuplexStreamingCall<RelayFrame, RelayFrame>? call = null;
        RelayFrameWriter? writer = null;
        RelayDuplexStream? stream = null;
        try
        {
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            lifetime.CancelAfter(TimeSpan.FromSeconds(capability.MaximumDurationSeconds));
            channel = CreateChannel(relayEndpoint);
            call = new Mk8.Drava.Transport.Protocol.V1.NodeRelay.NodeRelayClient(channel).Relay(cancellationToken: lifetime.Token);
            var activeCall = call;
            var accepted = await NegotiateAsync(call, capability, payload, RelayCapabilityProof.Sign(key, _siteId, payload), lifetime.Token).ConfigureAwait(false);
            lifetime.CancelAfter(TimeSpan.FromSeconds(accepted.MaximumDurationSeconds));
            writer = new RelayFrameWriter((frame, token) => activeCall.RequestStream.WriteAsync(frame, token), (int)accepted.StreamWindowFrames);
            // Successful publication transfers this stream to the connector's tracked cleanup task.
            // A failed publication instead awaits its asynchronous disposal in the finally below.
#pragma warning disable CA2000 // Conditional async disposal/ownership transfer is not recognized by this analyzer.
            stream = RelayDuplexStream.Create(call.ResponseStream, writer, Consumed.Types.Direction.Request, checked((long)accepted.MaximumBytesPerDirection), call.Dispose, lifetime.Token);
#pragma warning restore CA2000
            PublishOwner(capability.CapabilityId, stream, writer, call, channel, lifetime);
            var result = stream;
            stream = null; writer = null; call = null; channel = null; lifetime = null;
            return result;
        }
        finally
        {
            call?.Dispose();
            if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
            writer?.Dispose(); channel?.Dispose(); lifetime?.Dispose();
        }
    }

    private async Task<RelayAccepted> NegotiateAsync(AsyncDuplexStreamingCall<RelayFrame, RelayFrame> call, RelayCapability capability, byte[] payload, byte[] signature, CancellationToken cancellationToken)
    {
        using var opening = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        opening.CancelAfter(TimeSpan.FromSeconds(_policy.OpeningTimeoutSeconds));
        var identity = capability.Identity;
        await call.RequestStream.WriteAsync(new RelayFrame { Open = new RelayOpen { Version = 1, SiteId = _siteId, NodeId = identity.NodeId, InstanceId = identity.InstanceId,
            BootId = identity.BootId, ExchangeId = capability.ExchangeId, StreamWindowFrames = (uint)_policy.StreamWindowFrames,
            CapabilityJson = ByteString.CopyFrom(payload), Signature = ByteString.CopyFrom(signature) } }, opening.Token).ConfigureAwait(false);
        if (!await call.ResponseStream.MoveNext(opening.Token).ConfigureAwait(false) || call.ResponseStream.Current.Accepted is not { Version: 1 } accepted ||
            !string.Equals(accepted.CapabilityId, capability.CapabilityId, StringComparison.Ordinal) || accepted.StreamWindowFrames is < 1 || accepted.StreamWindowFrames > _policy.StreamWindowFrames ||
            accepted.MaximumBytesPerDirection is < 1 || accepted.MaximumBytesPerDirection > (ulong)capability.MaximumBytesPerDirection ||
            accepted.MaximumDurationSeconds is < 1 || accepted.MaximumDurationSeconds > capability.MaximumDurationSeconds) throw new InvalidDataException("Node did not acknowledge bounded relay limits for this capability.");
        return accepted;
    }

    private GrpcChannel CreateChannel(NodeRelayEndpoint endpoint)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(_policy.OpeningTimeoutSeconds), MaxConnectionsPerServer = 1,
            SslOptions = new SslClientAuthenticationOptions { ClientCertificates = new X509CertificateCollection { _controller },
                RemoteCertificateValidationCallback = (_, certificate, _, errors) => ValidateNode(certificate, errors, endpoint) },
        };
        var host = IPAddress.Parse(endpoint.Address).AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + endpoint.Address + "]" : endpoint.Address;
        try { return GrpcChannel.ForAddress($"https://{host}:{endpoint.Port}", new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true, MaxReceiveMessageSize = 64 * 1024, MaxSendMessageSize = 64 * 1024 }); }
        catch { handler.Dispose(); throw; }
    }

    private bool ValidateNode(X509Certificate? certificate, SslPolicyErrors errors, NodeRelayEndpoint endpoint)
    {
        if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != SslPolicyErrors.None) return false;
        using var leaf = new X509Certificate2(certificate);
        return NodeCertificateTrust.Validate(leaf, _root, endpoint.Address, endpoint.CertificateFingerprint, _clock);
    }

    private void PublishOwner(string id, RelayDuplexStream stream, RelayFrameWriter writer, AsyncDuplexStreamingCall<RelayFrame, RelayFrame> call, GrpcChannel channel, CancellationTokenSource lifetime)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _owned.Add(id, FinishOwnerAsync(published.Task, id, stream, writer, call, channel, lifetime));
            published.SetResult();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The publication signal and relay pump belong to this connector's admitted connection. Shutdown and disposal join this tracked task before releasing transport resources; all continuations avoid UI context.")]
    private async Task FinishOwnerAsync(Task published, string id, RelayDuplexStream stream, RelayFrameWriter writer, AsyncDuplexStreamingCall<RelayFrame, RelayFrame> call, GrpcChannel channel, CancellationTokenSource lifetime)
    {
        await published.ConfigureAwait(false);
        try { await stream.WaitForCompletionAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or RpcException or OperationCanceledException) { }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            writer.Dispose(); call.Dispose(); channel.Dispose(); lifetime.Dispose();
            _admission.Release();
            lock (_gate) { _owned.Remove(id); SignalQuiescence(); }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is null) { _disposed = true; SignalQuiescence(); _disposeTask = DisposeCoreAsync(); }
            return new ValueTask(_disposeTask);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The admitted connection tasks are created, tracked and owned by this connector, with asynchronous private cancellation. All concurrent disposal calls join this same owned task before admission and controller lifetime state are disposed.")]
    private async Task DisposeCoreAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _quiesced.Task.ConfigureAwait(false);
        _shutdown.Dispose(); _admission.Dispose();
    }

    private void SignalQuiescence()
    {
        if (_disposed && _opening == 0 && _owned.Count == 0) _quiesced.TrySetResult();
    }
}
