using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Mk8.Drava.Application.INF.Proxy.Health;
public sealed class UpstreamHealthCheckService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private readonly IUpstreamHealthCheckTargetSource _targetSource;
    private readonly UpstreamHealthCheckCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    public UpstreamHealthCheckService(IUpstreamHealthCheckTargetSource targetSource, UpstreamHealthCheckCoordinator coordinator, TimeProvider timeProvider)
    {
        _targetSource = targetSource;
        _coordinator = coordinator;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _coordinator.RunDueChecksAsync(_targetSource.ReadTargets(), stoppingToken).ConfigureAwait(false);
            await Task.Delay(PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }
}
