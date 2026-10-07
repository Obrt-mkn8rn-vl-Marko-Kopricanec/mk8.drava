namespace Mk8.Drava.Application.INF.Acme;

public sealed partial class AcmeRenewalService
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10003, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "ACME renewal check failed unexpectedly.", SkipEnabledCheck = true)]
    private static partial void LogACMERenewalCheckFailedUnexpectedly10003(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);
}
