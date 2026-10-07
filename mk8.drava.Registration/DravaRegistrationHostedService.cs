using Grpc.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Hosting;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Registration;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.Registration;

internal sealed class DravaRegistrationHostedService(DravaRegistrationOptions options, DravaRegistrationState state, IHostApplicationLifetime lifetime, IServer server) : BackgroundService
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _operations = new(1, 1);
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
    private int _draining;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var started = lifetime.ApplicationStarted.Register(() => _started.TrySetResult());
        await _started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested && Volatile.Read(ref _draining) == 0)
        {
            var delay = TimeSpan.FromSeconds(options.PendingRetrySeconds);
            await _operations.WaitAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _draining) != 0) return;
                _channel ??= await RegistrationGatewayFinder.FindAsync(options, stoppingToken).ConfigureAwait(false);
                if (_channel is null) state.Failed();
                else
                {
                    var advertisement = await RenewAgentAsync(_channel, stoppingToken).ConfigureAwait(false);
                    _registerAttempted = true;
                    var result = await _channel.SubmitAsync(new RegistrationCommand { Identity = _identity, Operation = _registered ? RegistrationOperation.Renew : RegistrationOperation.Register, Advertisement = _registered ? null : advertisement }, stoppingToken).ConfigureAwait(false);
                    _registered = true;
                    state.Accept(result);
                    delay = RenewalDelay(options, result, _agent?.Descriptor);
                }
            }
            catch (Exception exception) when (exception is RpcException or HttpRequestException or InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                _channel?.Dispose(); _channel = null; _registered = false; state.Failed();
            }
            finally { _operations.Release(); }
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal static TimeSpan RenewalDelay(DravaRegistrationOptions settings, RegistrationStatus status, Mk8.Drava.Configuration.NodeAgentDescriptor? agent)
    {
        var seconds = Math.Min(status.RenewAfterSeconds, (agent?.MappingLeaseSeconds ?? 300) / 3d);
        if (status.Phase != RegistrationPhase.Ready) seconds = Math.Min(seconds, settings.PendingRetrySeconds);
        var jitter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100 - settings.RenewJitterPercent, 101 + settings.RenewJitterPercent) / 100d;
        return TimeSpan.FromSeconds(seconds * jitter);
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
                _agent?.Dispose(); _agent = new NodeAgentLocalClient(descriptor);
                _registered = false;
            }
            var relay = await _agent.ApplyAsync(new RegistrationCommand { Identity = _identity, Operation = RegistrationOperation.Register, Advertisement = advertisement }, cancellationToken).ConfigureAwait(false);
            advertisement = advertisement with { Relay = relay };
        }
        if (_advertisement != advertisement) _registered = false;
        _advertisement = advertisement;
        return advertisement;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _draining, 1);
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drain.CancelAfter(TimeSpan.FromMilliseconds(options.ShutdownDeadlineMilliseconds));
        try
        {
            await base.StopAsync(drain.Token).ConfigureAwait(false);
            await _operations.WaitAsync(drain.Token).ConfigureAwait(false);
            try
            {
                if (_agent is not null)
                    await DrainAgentAsync(_agent, drain.Token).ConfigureAwait(false);
                if (_registerAttempted)
                {
                    _channel ??= await RegistrationGatewayFinder.FindAsync(options, drain.Token).ConfigureAwait(false);
                    if (_channel is null) state.Failed();
                    else state.Accept(await _channel.SubmitAsync(new RegistrationCommand { Identity = _identity, Operation = RegistrationOperation.Drain }, drain.Token).ConfigureAwait(false));
                }
            }
            finally { _operations.Release(); }
        }
        catch (Exception exception) when (exception is RpcException or HttpRequestException or InvalidDataException or IOException or System.Text.Json.JsonException or OperationCanceledException) { state.Failed(); }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DrainAgentAsync(NodeAgentLocalClient agent, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.AgentDrainDeadlineMilliseconds));
        try { await agent.ApplyAsync(new RegistrationCommand { Identity = _identity, Operation = RegistrationOperation.Drain }, timeout.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is RpcException or InvalidDataException or IOException or System.Text.Json.JsonException or OperationCanceledException) { }
    }

    public override void Dispose() { base.Dispose(); _agent?.Dispose(); _channel?.Dispose(); _operations.Dispose(); }
}
