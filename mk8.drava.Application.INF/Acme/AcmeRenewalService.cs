using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Acme;
public sealed partial class AcmeRenewalService : BackgroundService
{
    private readonly IAcmeRenewalScheduleInputSource _scheduleInputSource;
    private readonly AcmeCertificateManager _manager;
    private readonly AcmeRenewalSchedulePolicy _schedulePolicy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AcmeRenewalService> _logger;
    public AcmeRenewalService(IAcmeRenewalScheduleInputSource scheduleInputSource, AcmeCertificateManager manager, AcmeRenewalSchedulePolicy schedulePolicy, TimeProvider timeProvider, ILogger<AcmeRenewalService> logger)
    {
        _scheduleInputSource = scheduleInputSource;
        _manager = manager;
        _schedulePolicy = schedulePolicy;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _manager.CheckRenewalsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            #pragma warning disable CA1031 // Observe unexpected check failure through the warning logger while retaining the configured periodic retry and independent host lifetime.
            catch (Exception exception)
            {
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogACMERenewalCheckFailedUnexpectedly10003(_logger, exception);
                }
            }
            #pragma warning restore CA1031

            var delay = ResolveDelay();
            try
            {
                await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private TimeSpan ResolveDelay()
    {
        return _schedulePolicy.ResolveDelay(_scheduleInputSource.ReadInput());
    }
}
