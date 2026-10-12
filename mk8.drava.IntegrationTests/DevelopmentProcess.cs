using System.Diagnostics;
using System.Text;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _stdout;
    private readonly Task _stderr;
    private readonly StringBuilder _log = new();
    private int _disposed;

    public DevelopmentProcess(string assemblyPath, string bootstrapPath, string bootstrapOption = "--bootstrap", IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(assemblyPath);
        start.ArgumentList.Add(bootstrapOption);
        start.ArgumentList.Add(bootstrapPath);
        if (environment is not null)
            foreach (var entry in environment) start.Environment[entry.Key] = entry.Value;
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the development process.");
        _stdout = CaptureAsync(_process.StandardOutput);
        _stderr = CaptureAsync(_process.StandardError);
    }

    public bool HasExited => _process.HasExited;

    public DevelopmentProcessMemory ReadMemory()
    {
        _process.Refresh();
        return new DevelopmentProcessMemory(_process.Id, _process.WorkingSet64, _process.PrivateMemorySize64);
    }

    public void ThrowIfExited()
    {
        if (!_process.HasExited) return;
        lock (_log) throw new InvalidOperationException($"Development process exited with {_process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}: {_log}");
    }

    public string CapturedLog { get { lock (_log) return _log.ToString(); } }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    private async Task CaptureAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            lock (_log)
            {
                _log.Append(buffer, 0, count);
                if (_log.Length > 32768) _log.Remove(0, _log.Length - 32768);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The stdout/stderr tasks are started and owned by this process fixture. Teardown joins them before disposing their Process-owned readers; the fixture captures no UI context.")]
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(_stdout, _stderr).ConfigureAwait(false);
        _process.Dispose();
    }
}
