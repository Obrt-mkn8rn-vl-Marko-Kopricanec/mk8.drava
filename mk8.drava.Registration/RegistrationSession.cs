using Grpc.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Registration;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.Registration;

// The hosted service's execution task owns this session through renewal, drain and disposal.
internal sealed class RegistrationSession(DravaRegistrationOptions options, IServer server) : IRegistrationSession
{
    private readonly RegistrationIdentity _identity = new()
    {
        SiteId = options.Site.SiteId, NodeId = options.NodeId, OwnerId = options.OwnerId, ServiceId = options.ServiceId,
        ContractId = options.ContractId, InstanceId = options.InstanceId, BootId = Guid.NewGuid().ToString("N"),
    };
    private SiteRegistrationChannel? _channel;
    private NodeAgentLocalClient? _agent;
    private ServiceAdvertisement? _advertisement;
    private bool _usesAgent;
    private bool _registerAttempted;
    private bool _registered;

    public NodeAgentDescriptor? AgentDescriptor => _agent?.Descriptor;

    public async ValueTask<RegistrationStatus?> RenewAsync(CancellationToken cancellationToken)
    {
        try
        {
            _channel ??= await RegistrationGatewayFinder.FindAsync(options, cancellationToken).ConfigureAwait(false);
            if (_channel is null) return null;
            var advertisement = await RenewAgentAsync(_channel, cancellationToken).ConfigureAwait(false);
            _registerAttempted = true;
            var result = await _channel.SubmitAsync(new RegistrationCommand
            {
                Identity = _identity, Operation = _registered ? RegistrationOperation.Renew : RegistrationOperation.Register,
                Advertisement = _registered ? null : advertisement,
            }, cancellationToken).ConfigureAwait(false);
            _registered = true;
            return result;
        }
        catch (Exception exception) when (exception is RpcException or HttpRequestException or InvalidDataException or IOException or System.Text.Json.JsonException)
        {
            _channel?.Dispose();
            _channel = null;
            _registered = false;
            throw;
        }
    }

    private async ValueTask<ServiceAdvertisement> RenewAgentAsync(SiteRegistrationChannel channel, CancellationToken cancellationToken)
    {
        var advertisement = BoundServiceAdvertisement.Read(server, options, channel.LocalAddress);
        var descriptor = await NodeAgentLocalClient.ReadDescriptorAsync(options.Site, options.NodeId, channel.NodeCertificateFingerprint, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
        {
            if (_usesAgent) throw new InvalidDataException("The enrolled local agent is unavailable.");
        }
        else
        {
            _usesAgent = true;
            if (_agent?.Descriptor != descriptor)
            {
                _agent?.Dispose();
                _agent = null;
                _agent = new NodeAgentLocalClient(descriptor, options.NodeAgentToken);
                _registered = false;
            }
            var relay = await _agent.ApplyAsync(new RegistrationCommand { Identity = _identity, Operation = RegistrationOperation.Register, Advertisement = advertisement }, cancellationToken).ConfigureAwait(false);
            advertisement = advertisement with { Relay = relay };
        }
        if (_advertisement != advertisement) _registered = false;
        _advertisement = advertisement;
        return advertisement;
    }

    public async ValueTask<RegistrationStatus?> DrainAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_agent is not null) await DrainAgentAsync(_agent, cancellationToken).ConfigureAwait(false);
        if (!_registerAttempted) return null;
        _channel ??= await RegistrationGatewayFinder.FindAsync(options, cancellationToken).ConfigureAwait(false);
        return _channel is null ? null : await _channel.SubmitAsync(new RegistrationCommand
        {
            Identity = _identity, Operation = RegistrationOperation.Drain,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DrainAgentAsync(NodeAgentLocalClient agent, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.AgentDrainDeadlineMilliseconds));
        try { await agent.ApplyAsync(new RegistrationCommand { Identity = _identity, Operation = RegistrationOperation.Drain }, timeout.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is RpcException or InvalidDataException or IOException or System.Text.Json.JsonException or OperationCanceledException) { }
    }

    public void Dispose()
    {
        _agent?.Dispose();
        _channel?.Dispose();
    }
}
