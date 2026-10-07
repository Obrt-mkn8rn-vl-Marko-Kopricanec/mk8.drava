namespace Mk8.Drava.Application.INF.Observability;

public sealed partial class AcmeCertificateRenewalLogger
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10008, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "ACME renewal for certificate {CertificateId} failed: {ErrorSummary}", SkipEnabledCheck = true)]
    private static partial void LogACMERenewalForCertificateFailed10008(global::Microsoft.Extensions.Logging.ILogger logger, string certificateId, string? errorSummary, global::System.Exception? exception);
}
