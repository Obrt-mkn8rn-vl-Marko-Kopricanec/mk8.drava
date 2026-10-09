using System.Diagnostics;

namespace Mk8.Drava.Gateway.Hosting;

internal sealed partial class GatewayStartupProgress : IDisposable
{
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly ILoggerFactory _factory;
    private readonly ILogger<GatewayStartupProgress> _logger;
    private int _disposed;

    public GatewayStartupProgress()
    {
        _factory = LoggerFactory.Create(static logging => logging.AddSimpleConsole(static options =>
        {
            options.SingleLine = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z' ";
        }));
        try { _logger = _factory.CreateLogger<GatewayStartupProgress>(); }
        catch { _factory.Dispose(); throw; }
    }

    public void Enter(GatewayStartupPhase phase)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        LogStartupPhase(_logger, phase, (long)Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds);
    }

    public void Complete()
    {
        Enter(GatewayStartupPhase.HostStarted);
        Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _factory.Dispose();
    }

    [LoggerMessage(10068, LogLevel.Information, "Gateway startup entered {Phase} after {ElapsedMilliseconds} ms.")]
    private static partial void LogStartupPhase(ILogger logger, GatewayStartupPhase phase, long elapsedMilliseconds);
}
