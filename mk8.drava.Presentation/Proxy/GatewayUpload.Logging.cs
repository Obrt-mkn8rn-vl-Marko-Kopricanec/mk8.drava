using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Presentation.Proxy;

public sealed partial class GatewayUpload
{
    // Aggregate counters only: never record body, target, authorization or client identity.
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Upload counters HTTPS={Https} readBytes={ReadBytes} writtenFrames={WrittenFrames} smallFrames={SmallFrames} maximumFrameBytes={MaximumFrameBytes} endOfStream={EndOfStream}")]
    private static partial void LogUploadCounters(ILogger logger, bool https, ulong readBytes, long writtenFrames,
        long smallFrames, int maximumFrameBytes, bool endOfStream);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Upload timing readMilliseconds={ReadMilliseconds} rpcWriteMilliseconds={RpcWriteMilliseconds}")]
    private static partial void LogUploadTiming(ILogger logger, double readMilliseconds, double rpcWriteMilliseconds);
}
