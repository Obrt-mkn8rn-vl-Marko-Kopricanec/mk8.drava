using System.Diagnostics;

namespace Mk8.Drava.Application.Hosting;

// The bootstrap logger exists before the service provider and closes when hosting starts.
internal sealed partial class ApplicationStartupProgress : IDisposable
{
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly ILoggerFactory _factory;
    private readonly ILogger<ApplicationStartupProgress> _logger;
    private int _disposed;

    public ApplicationStartupProgress()
    {
        _factory = LoggerFactory.Create(static logging => logging.AddSimpleConsole(static options =>
        {
            options.SingleLine = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z' ";
        }));
        try { _logger = _factory.CreateLogger<ApplicationStartupProgress>(); }
        catch { _factory.Dispose(); throw; }
    }

    public void Enter(ApplicationStartupPhase phase)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        LogStartupPhase(_logger, phase, (long)Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds);
    }

    public void Complete()
    {
        Enter(ApplicationStartupPhase.HostStarted);
        Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _factory.Dispose();
    }

    [LoggerMessage(10067, LogLevel.Information, "Application startup entered {Phase} after {ElapsedMilliseconds} ms.")]
    private static partial void LogStartupPhase(ILogger logger, ApplicationStartupPhase phase, long elapsedMilliseconds);
}
