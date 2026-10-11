using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class ProxyForwarder
{
    // Aggregate timing only: no target, authentication fields or body data.
    [LoggerMessage(10070, LogLevel.Debug, "Known upload counters declaredBytes={DeclaredBytes} writtenBytes={WrittenBytes} writes={Writes} smallWrites={SmallWrites} maximumWriteBytes={MaximumWriteBytes} completed={Completed}")]
    private static partial void LogKnownUploadCounters(ILogger logger, long? declaredBytes, long writtenBytes, long writes,
        long smallWrites, int maximumWriteBytes, bool completed);

    [LoggerMessage(10071, LogLevel.Debug, "Known upload timing totalMilliseconds={TotalMilliseconds} originWriteMilliseconds={OriginWriteMilliseconds}")]
    private static partial void LogKnownUploadTiming(ILogger logger, double totalMilliseconds, double originWriteMilliseconds);
}
