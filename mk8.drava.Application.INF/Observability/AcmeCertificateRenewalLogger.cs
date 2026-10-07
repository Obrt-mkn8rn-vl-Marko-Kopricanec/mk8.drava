using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Observability;
public sealed partial class AcmeCertificateRenewalLogger : IAcmeCertificateRenewalEventSink
{
    private readonly ILogger<AcmeCertificateRenewalLogger> _logger;
    public AcmeCertificateRenewalLogger(ILogger<AcmeCertificateRenewalLogger> logger)
    {
        _logger = logger;
    }

    public void RenewalFailed(string certificateId, string? errorSummary)
    {
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
        {
            LogACMERenewalForCertificateFailed10008(_logger, certificateId, errorSummary, null);
        }
    }
}
