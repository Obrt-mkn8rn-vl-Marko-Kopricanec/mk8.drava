using Grpc.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Hosting;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Registration;

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
    private bool _registerAttempted;
    private bool _registered;
    private int _draining;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var started = lifetime.ApplicationStarted.Register(() => _started.TrySetResult());
        await _started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested && Volatile.Read(ref _draining) == 0)
        {
            var delay = TimeSpan.FromSeconds(5);
            await _operations.WaitAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _draining) != 0) return;
                _channel ??= await RegistrationGatewayFinder.FindAsync(options, stoppingToken).ConfigureAwait(false);
                if (_channel is null) state.Failed();
                else
                {
                    var advertisement = _registered ? null : BoundServiceAdvertisement.Read(server, options, _channel.LocalAddress);
                    _registerAttempted = true;
                    var result = await _channel.SubmitAsync(new RegistrationCommand { Identity = _identity, Operation = _registered ? RegistrationOperation.Renew : RegistrationOperation.Register, Advertisement = advertisement }, stoppingToken).ConfigureAwait(false);
                    _registered = true;
                    state.Accept(result);
                    delay = TimeSpan.FromSeconds((result.Phase == RegistrationPhase.Ready ? result.RenewAfterSeconds : 5) * (System.Security.Cryptography.RandomNumberGenerator.GetInt32(800, 1201) / 1000d));
                }
            }
            catch (Exception exception) when (exception is RpcException or HttpRequestException or InvalidDataException or IOException)
            {
                _channel?.Dispose(); _channel = null; _registered = false; state.Failed();
            }
            finally { _operations.Release(); }
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _draining, 1);
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drain.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await _operations.WaitAsync(drain.Token).ConfigureAwait(false);
            try
            {
                if (_registerAttempted && _channel is not null)
                    state.Accept(await _channel.SubmitAsync(new RegistrationCommand { Identity = _identity, Operation = RegistrationOperation.Drain }, drain.Token).ConfigureAwait(false));
            }
            finally { _operations.Release(); }
        }
        catch (Exception exception) when (exception is RpcException or HttpRequestException or InvalidDataException or OperationCanceledException) { }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose() { base.Dispose(); _channel?.Dispose(); _operations.Dispose(); }
}
