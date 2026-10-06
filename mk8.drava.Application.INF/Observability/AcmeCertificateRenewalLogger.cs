using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Observability;
public sealed class AcmeCertificateRenewalLogger : IAcmeCertificateRenewalEventSink
{
    private readonly ILogger<AcmeCertificateRenewalLogger> _logger;
    public AcmeCertificateRenewalLogger(ILogger<AcmeCertificateRenewalLogger> logger)
    {
        _logger = logger;
    }

    public void RenewalFailed(string certificateId, string? errorSummary)
    {
        _logger.LogWarning("ACME renewal for certificate {CertificateId} failed: {ErrorSummary}", certificateId, errorSummary);
    }
}
