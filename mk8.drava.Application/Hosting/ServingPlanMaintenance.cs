using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.Hosting;

internal sealed class ServingPlanMaintenance(ServingPlanState plans, RegistryCoordinator registry, Mk8.Drava.Configuration.ApplicationBootstrap bootstrap, ILogger<ServingPlanMaintenance> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> RenewalFailed = LoggerMessage.Define(LogLevel.Warning, new EventId(1, nameof(RenewalFailed)),
        "Serving certificate renewal failed; retaining the last issued plan.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(bootstrap.Controller!.ServingPlan.RenewalCheckSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!registry.StorageHealthy) continue;
            try { await plans.RenewIfRequiredAsync(stoppingToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
            { RenewalFailed(logger, exception); }
        }
    }
}
