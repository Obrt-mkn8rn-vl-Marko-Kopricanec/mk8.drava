using Grpc.Core;
using Mk8.Drava.Configuration;
using Mk8.Drava.Presentation.Certificates;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Gateway.Hosting;

internal sealed class GatewayPlanService(GatewayBootstrap bootstrap, ApplicationChannel channel, GatewayMaterialState material, GatewayPlanCache cache,
    IHostApplicationLifetime lifetime, ILogger<GatewayPlanService> logger) : BackgroundService
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly Action<ILogger, Exception?> PlanUnavailable = LoggerMessage.Define(LogLevel.Warning, new EventId(1, nameof(PlanUnavailable)),
        "Application presentation plan is unavailable; retaining valid installed material.");
    private static readonly Action<ILogger, ulong, DateTimeOffset, Exception?> PlanInstalled = LoggerMessage.Define<ulong, DateTimeOffset>(LogLevel.Information,
        new EventId(10069, nameof(PlanInstalled)), "Gateway installed presentation generation {Generation} at {InstalledAtUtc}.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var registration = lifetime.ApplicationStarted.Register(() => _started.TrySetResult());
        if (lifetime.ApplicationStarted.IsCancellationRequested) _started.TrySetResult();
        await _started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
        var available = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = RefreshDelay(bootstrap.Plan, material.Read());
            try { await RefreshAsync(stoppingToken).ConfigureAwait(false); available = true; delay = RefreshDelay(bootstrap.Plan, material.Read()); }
            catch (Exception exception) when (exception is RpcException or IOException or InvalidDataException or System.Security.Cryptography.CryptographicException)
            {
                if (available) PlanUnavailable(logger, exception);
                available = false; delay = TimeSpan.FromSeconds(bootstrap.Plan.RetrySeconds);
            }
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal static TimeSpan RefreshDelay(GatewayPlanSettings settings, PresentationPlan? plan) =>
        TimeSpan.FromSeconds(Math.Min(settings.RefreshSeconds, (plan?.AcknowledgmentLeaseSeconds is > 0 ? plan.AcknowledgmentLeaseSeconds : 30) / 3d));

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var client = new ApplicationControl.ApplicationControlClient(channel.Invoker);
        using var call = client.GatewayPlanAsync(new GatewayIdentity { Version = 1, GatewayId = bootstrap.GatewayId }, channel.Credentials,
            deadline: DateTime.UtcNow.AddSeconds(bootstrap.Plan.RequestDeadlineSeconds), cancellationToken: cancellationToken);
        var plan = await call.ResponseAsync.ConfigureAwait(false);
        if (!material.Matches(plan)) await InstallAsync(plan, cancellationToken).ConfigureAwait(false);
        var actual = material.Read() ?? throw new InvalidDataException("Applied presentation material is absent.");
        using var acknowledgment = client.AcknowledgePlanAsync(new PlanAcknowledgment { Version = 1, GatewayId = actual.GatewayId, Generation = actual.Generation,
            ContentSha256 = actual.ContentSha256, Applied = true }, channel.Credentials, deadline: DateTime.UtcNow.AddSeconds(bootstrap.Plan.RequestDeadlineSeconds), cancellationToken: cancellationToken);
        if ((await acknowledgment.ResponseAsync.ConfigureAwait(false)).StatusCode != 200) throw new InvalidDataException("Application rejected the installed presentation generation.");
    }

    private async Task InstallAsync(PresentationPlan plan, CancellationToken cancellationToken)
    {
        GatewayServingMaterial? candidate = new(plan, bootstrap);
        try
        {
            material.RequireInstallable(candidate.Plan);
            await cache.WriteAsync(plan, cancellationToken).ConfigureAwait(false);
            material.Install(candidate);
            candidate = null;
            PlanInstalled(logger, plan.Generation, DateTimeOffset.UtcNow, null);
        }
        finally { candidate?.Dispose(); }
    }
}
