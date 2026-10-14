using System.Diagnostics;
using System.Text;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string? _outputDirectory;
    private readonly Task _stdout;
    private readonly Task _stderr;
    private readonly StringBuilder _log = new();
    private readonly Lock _retirementGate = new();
    private Task<string?>? _retirementTask;
    private string? _retainedDirectory;

    public DevelopmentProcess(string assemblyPath, string bootstrapPath, string bootstrapOption = "--portable-service", IReadOnlyDictionary<string, string>? environment = null, bool templateOnly = false)
    {
        var portable = bootstrapOption is "--portable-service" or "--node-agent" or "--initialize-site" or "--development-registration-sdk";
        if (portable)
        {
            _outputDirectory = PrepareOutputRoot(assemblyPath, bootstrapPath, templateOnly);
            assemblyPath = Path.Combine(_outputDirectory, Path.GetFileName(assemblyPath));
        }
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(assemblyPath);
        if (portable)
        {
            if (!string.Equals(bootstrapOption, "--portable-service", StringComparison.Ordinal)) start.ArgumentList.Add(bootstrapOption);
        }
        else
        {
            start.ArgumentList.Add(bootstrapOption);
            start.ArgumentList.Add(bootstrapPath);
        }
        if (environment is not null)
            foreach (var entry in environment) start.Environment[entry.Key] = entry.Value;
        try { _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the development process."); }
        catch
        {
            if (_outputDirectory is not null) Directory.Delete(_outputDirectory, recursive: true);
            throw;
        }
        _stdout = CaptureAsync(_process.StandardOutput);
        _stderr = CaptureAsync(_process.StandardError);
    }

    private static string PrepareOutputRoot(string assemblyPath, string documentPath, bool templateOnly)
    {
        var root = Directory.CreateTempSubdirectory("drava_role_").FullName;
        try
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var source = Path.GetDirectoryName(assemblyPath) ?? throw new InvalidDataException("Development output directory is missing.");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                if (relative.StartsWith("Environment" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                var target = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var environment = Directory.CreateDirectory(Path.Combine(root, "Environment")).FullName;
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(environment, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.Copy(documentPath, Path.Combine(environment, templateOnly ? ".env.template" : ".env"));
            var key = Path.Combine(Path.GetDirectoryName(documentPath)!, ".installation.key");
            if (File.Exists(key)) File.Copy(key, Path.Combine(environment, ".installation.key"));
            return root;
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    public bool HasExited => _process.HasExited;
    internal int ProcessId => _process.Id;
    internal int? TerminationExitCode { get; private set; }
    internal bool TerminatedByFixture { get; private set; }

    internal string PortableDocumentPath => Path.Combine(_outputDirectory
        ?? throw new InvalidOperationException("This fixture has no portable installation."), "Environment", ".env");

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

    public async Task<string> RetirePortableAsync(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (_outputDirectory is null || !Path.IsPathFullyQualified(destinationDirectory))
            throw new InvalidOperationException("Portable retirement requires an owned installation and an absolute destination.");
        return await RetireAsync(destinationDirectory).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The development installation was retired without a retained collection.");
    }

    public async ValueTask DisposeAsync() => _ = await RetireAsync(destinationDirectory: null).ConfigureAwait(false);

    private ValueTask<string?> RetireAsync(string? destinationDirectory)
    {
        lock (_retirementGate)
        {
            if (_retirementTask is not null)
            {
                if (destinationDirectory is not null && !string.Equals(destinationDirectory, _retainedDirectory, StringComparison.Ordinal))
                    throw new InvalidOperationException("The development installation already has another retirement destination.");
                return new ValueTask<string?>(_retirementTask);
            }
            _retainedDirectory = destinationDirectory;
            _retirementTask = RetireCoreAsync(destinationDirectory);
            return new ValueTask<string?>(_retirementTask);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The stdout/stderr tasks are started and owned by this process fixture. Teardown joins them before disposing their Process-owned readers; the fixture captures no UI context.")]
    private async Task<string?> RetireCoreAsync(string? destinationDirectory)
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                TerminatedByFixture = true;
            }
            await _process.WaitForExitAsync().ConfigureAwait(false);
            TerminationExitCode = _process.ExitCode;
            await Task.WhenAll(_stdout, _stderr).ConfigureAwait(false);
            if (destinationDirectory is null) return null;
            Directory.CreateDirectory(destinationDirectory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destinationDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var document = PortableDocumentPath;
            var retained = Path.Combine(destinationDirectory, ".env");
            File.Copy(document, retained, overwrite: true);
            File.Copy(Path.Combine(Path.GetDirectoryName(document)!, ".installation.key"), Path.Combine(destinationDirectory, ".installation.key"), overwrite: true);
            return retained;
        }
        finally
        {
            _process.Dispose();
            if (_outputDirectory is not null) Directory.Delete(_outputDirectory, recursive: true);
        }
    }
}
