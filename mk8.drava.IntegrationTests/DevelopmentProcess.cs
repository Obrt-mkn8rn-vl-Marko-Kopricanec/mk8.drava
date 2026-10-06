using System.Diagnostics;
using System.Text;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _stdout;
    private readonly Task _stderr;
    private readonly StringBuilder _log = new();

    public DevelopmentProcess(string assemblyPath, string bootstrapPath)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(assemblyPath);
        start.ArgumentList.Add("--bootstrap");
        start.ArgumentList.Add(bootstrapPath);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the development process.");
        _stdout = CaptureAsync(_process.StandardOutput);
        _stderr = CaptureAsync(_process.StandardError);
    }

    public void ThrowIfExited()
    {
        if (!_process.HasExited) return;
        lock (_log) throw new InvalidOperationException($"Development process exited with {_process.ExitCode}: {_log}");
    }

    public string CapturedLog { get { lock (_log) return _log.ToString(); } }

    private async Task CaptureAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            lock (_log)
            {
                _log.Append(buffer, 0, count);
                if (_log.Length > 32768) _log.Remove(0, _log.Length - 32768);
            }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The stdout/stderr tasks are started and owned by this process fixture. Teardown joins them before disposing their Process-owned readers; the fixture captures no UI context.")]
    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(_stdout, _stderr).ConfigureAwait(false);
        _process.Dispose();
    }
}
