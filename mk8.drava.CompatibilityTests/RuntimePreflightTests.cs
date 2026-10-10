using System.Text.Json;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class RuntimePreflightTests
{
    public static void RuntimePreflightCreatesOwnedDirectoriesSafely()
    {
        using var temp = TemporaryDirectory.Create();
        var generatedAtUtc = new DateTimeOffset(2026, 6, 10, 7, 45, 0, TimeSpan.Zero);
        var service = new ProxyRuntimePreflightService(Provider(temp.Path), new ProxyDataDirectoryPathSafety(), new ProxyRuntimeDirectoryProbe(), new FixedTimeProvider(generatedAtUtc));
        var status = service.RunStartupChecks();
        AssertEx.Equal("healthy", status.State);
        AssertEx.Equal(generatedAtUtc, status.GeneratedAtUtc);
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "config")));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "config", "sites")));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "logs")));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "certs")));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "state")));
        AssertEx.True(status.Checks.All(static check => !Path.IsPathRooted(check.RelativePath)));
        AssertEx.True(status.Checks.All(static check => string.Equals(check.Reason, "ok", StringComparison.Ordinal)));
    }

    public static void RuntimePreflightReportsUnwritableDirectorySafely()
    {
        const string secret = "phase-52-directory-secret";
        using var temp = TemporaryDirectory.Create();
        var probe = new DelegateProbe(path => path.EndsWith("logs", StringComparison.OrdinalIgnoreCase) ? ProxyRuntimeDirectoryProbeResult.Probed(created: false, canRead: true, canWrite: false) : ProxyRuntimeDirectoryProbeResult.Probed(created: false, canRead: true, canWrite: true));
        var service = new ProxyRuntimePreflightService(Provider(temp.Path), new ProxyDataDirectoryPathSafety(), probe, TimeProvider.System);
        var status = service.RunStartupChecks();
        var text = JsonSerializer.Serialize(status);
        AssertEx.Equal("degraded", status.State);
        AssertEx.True(status.Reasons.Contains("directory_not_writable", StringComparer.Ordinal), string.Join(",", status.Reasons));
        AssertEx.True(status.Checks.Any(static check => string.Equals(check.Name, "logs_directory", StringComparison.Ordinal) && string.Equals(check.Severity, "warning", StringComparison.Ordinal) && string.Equals(check.Reason, "directory_not_writable", StringComparison.Ordinal)));
        AssertEx.False(text.Contains(secret, StringComparison.Ordinal), text);
        AssertEx.False(text.Contains(temp.Path, StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("Authorization", StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("Cookie", StringComparison.OrdinalIgnoreCase), text);
    }

    public static void RuntimePreflightRejectsUnsafeChildPath()
    {
        using var temp = TemporaryDirectory.Create();
        var provider = new UnsafeLogsProvider(temp.Path, Path.Combine(Path.GetTempPath(), $"mdrava-outside-{Guid.NewGuid():N}"));
        var service = new ProxyRuntimePreflightService(provider, new ProxyDataDirectoryPathSafety(), new DelegateProbe(_ => ProxyRuntimeDirectoryProbeResult.Probed(created: false, canRead: true, canWrite: true)), TimeProvider.System);
        var status = service.RunStartupChecks();
        AssertEx.Equal("degraded", status.State);
        AssertEx.True(status.Reasons.Contains("unsafe_path", StringComparer.Ordinal), string.Join(",", status.Reasons));
        AssertEx.True(status.Checks.Any(static check => string.Equals(check.Name, "logs_directory", StringComparison.Ordinal) && string.Equals(check.RelativePath, "logs", StringComparison.Ordinal) && string.Equals(check.Reason, "unsafe_path", StringComparison.Ordinal) && string.Equals(check.Severity, "warning", StringComparison.Ordinal)));
    }

    public static void RuntimePreflightInspectDoesNotCreateMissingDirectories()
    {
        using var temp = TemporaryDirectory.Create();
        var service = new ProxyRuntimePreflightService(Provider(temp.Path), new ProxyDataDirectoryPathSafety(), new ProxyRuntimeDirectoryProbe(), TimeProvider.System);
        var status = service.Inspect();
        AssertEx.Equal("failed", status.State);
        AssertEx.True(status.Reasons.Contains("missing_directory", StringComparer.Ordinal), string.Join(",", status.Reasons));
        AssertEx.False(Directory.Exists(Path.Combine(temp.Path, "config")));
        AssertEx.False(Directory.Exists(Path.Combine(temp.Path, "logs")));
    }

    public static void RuntimePreflightProbePolicyClassifiesReasonAndSeverity()
    {
        var criticalMissing = ProxyRuntimePreflightProbePolicy.Classify(ProxyRuntimeDirectoryProbeResult.Missing(), critical: true);
        var nonCriticalDenied = ProxyRuntimePreflightProbePolicy.Classify(ProxyRuntimeDirectoryProbeResult.AccessDenied(exists: true, created: false), critical: false);
        var healthy = ProxyRuntimePreflightProbePolicy.Classify(ProxyRuntimeDirectoryProbeResult.Probed(created: false, canRead: true, canWrite: true), critical: true);
        AssertEx.Equal(ProxyStatusText.Error, criticalMissing.Severity);
        AssertEx.Equal("missing_directory", criticalMissing.Reason);
        AssertEx.Equal(ProxyStatusText.Warning, nonCriticalDenied.Severity);
        AssertEx.Equal("directory_access_denied", nonCriticalDenied.Reason);
        AssertEx.Equal(ProxyStatusText.Info, healthy.Severity);
        AssertEx.Equal(ProxyStatusText.Ok, healthy.Reason);
    }

    public static void RuntimePreflightFactsRejectBlankText()
    {
        AssertEx.Throws<ArgumentException>(() => new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.Config, Name: " ", RelativePath: "config", Critical: true));
        AssertEx.Throws<ArgumentException>(() => new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.Config, Name: "config_directory", RelativePath: " ", Critical: true));
        AssertEx.Throws<ArgumentException>(() => new ProxyRuntimePreflightProbeClassification(Severity: " ", Reason: "missing_directory"));
        AssertEx.Throws<ArgumentException>(() => new ProxyRuntimePreflightProbeClassification(Severity: ProxyStatusText.Error, Reason: " "));
        AssertEx.Throws<ArgumentException>(() => new ProxyRuntimePreflightCheck(Name: " ", RelativePath: "config", Exists: false, Created: false, CanRead: false, CanWrite: false, Severity: ProxyStatusText.Error, Reason: "missing_directory"));
        AssertEx.Throws<ArgumentException>(() => new ProxyRuntimePreflightCheck(Name: "config_directory", RelativePath: " ", Exists: false, Created: false, CanRead: false, CanWrite: false, Severity: ProxyStatusText.Error, Reason: "missing_directory"));
        AssertEx.Throws<ArgumentException>(() => new ProxyRuntimePreflightCheck(Name: "config_directory", RelativePath: "config", Exists: false, Created: false, CanRead: false, CanWrite: false, Severity: " ", Reason: "missing_directory"));
        AssertEx.Throws<ArgumentException>(() => new ProxyRuntimePreflightCheck(Name: "config_directory", RelativePath: "config", Exists: false, Created: false, CanRead: false, CanWrite: false, Severity: ProxyStatusText.Error, Reason: " "));
        var check = new ProxyRuntimePreflightCheck(Name: "config_directory", RelativePath: "config", Exists: false, Created: false, CanRead: false, CanWrite: false, Severity: ProxyStatusText.Error, Reason: "missing_directory");
        AssertEx.Equal("config_directory", check.Name);
        AssertEx.Equal("config", check.RelativePath);
        AssertEx.Equal(ProxyStatusText.Error, check.Severity);
        AssertEx.Equal("missing_directory", check.Reason);
    }

    public static void RuntimeDirectoryProbeResultNamesCommonOutcomes()
    {
        var missing = ProxyRuntimeDirectoryProbeResult.Missing();
        var createdWritable = ProxyRuntimeDirectoryProbeResult.Probed(created: true, canRead: true, canWrite: true);
        var existingNotWritable = ProxyRuntimeDirectoryProbeResult.Probed(created: false, canRead: true, canWrite: false);
        var accessDenied = ProxyRuntimeDirectoryProbeResult.AccessDenied(exists: true, created: false);
        var ioError = ProxyRuntimeDirectoryProbeResult.IoError(exists: false, created: false);
        AssertEx.False(missing.Exists);
        AssertEx.True(missing is ProxyRuntimeDirectoryProbeResult.MissingResult);
        AssertEx.True(createdWritable.Exists);
        AssertEx.True(createdWritable.Created);
        AssertEx.True(createdWritable is ProxyRuntimeDirectoryProbeResult.ProbedResult);
        AssertEx.True(existingNotWritable.Exists);
        AssertEx.False(existingNotWritable.CanWrite);
        AssertEx.True(existingNotWritable is ProxyRuntimeDirectoryProbeResult.ProbedResult);
        AssertEx.True(accessDenied.Exists);
        AssertEx.False(accessDenied.CanRead);
        AssertEx.True(accessDenied is ProxyRuntimeDirectoryProbeResult.AccessDeniedResult);
        AssertEx.False(ioError.Exists);
        AssertEx.True(ioError is ProxyRuntimeDirectoryProbeResult.IoErrorResult);
    }

    public static void RuntimePreflightDirectoryPolicyListsExpectedDirectories()
    {
        var directories = ProxyRuntimePreflightDirectoryPolicy.ExpectedDirectories();
        AssertEx.Equal(6, directories.Count);
        AssertEx.True(directories.Any(static directory => directory.Kind == ProxyRuntimePreflightDirectoryKind.Data && string.Equals(directory.Name, "data_directory", StringComparison.Ordinal) && string.Equals(directory.RelativePath, ".", StringComparison.Ordinal) && directory.Critical));
        AssertEx.True(directories.Any(static directory => directory.Kind == ProxyRuntimePreflightDirectoryKind.Sites && string.Equals(directory.Name, "sites_directory", StringComparison.Ordinal) && string.Equals(directory.RelativePath, "config/sites", StringComparison.Ordinal) && directory.Critical));
        AssertEx.True(directories.Any(static directory => directory.Kind == ProxyRuntimePreflightDirectoryKind.Logs && string.Equals(directory.Name, "logs_directory", StringComparison.Ordinal) && string.Equals(directory.RelativePath, "logs", StringComparison.Ordinal) && !directory.Critical));
    }

    public static void RuntimePreflightStatusBuilderBuildsStateAndBoundedReasons()
    {
        var generatedAtUtc = new DateTimeOffset(2026, 6, 12, 13, 0, 0, TimeSpan.Zero);
        var checks = new List<ProxyRuntimePreflightCheck>
        {
            new ProxyRuntimePreflightCheck("config_directory", "config", Exists: false, Created: false, CanRead: false, CanWrite: false, ProxyStatusText.Error, "missing_directory"),
            new ProxyRuntimePreflightCheck("logs_directory", "logs", Exists: true, Created: false, CanRead: true, CanWrite: false, ProxyStatusText.Warning, "directory_not_writable"),
            new ProxyRuntimePreflightCheck("state_directory", "state", Exists: false, Created: false, CanRead: false, CanWrite: false, ProxyStatusText.Warning, "missing_directory")
        };
        var status = ProxyRuntimePreflightStatusBuilder.Build(generatedAtUtc, checks.Select(static check => check), maxReasons: 1);
        checks.Clear();
        AssertEx.Equal(ProxyStatusText.Failed, status.State);
        AssertEx.Equal(generatedAtUtc, status.GeneratedAtUtc);
        AssertEx.Equal(1, status.Reasons.Count);
        AssertEx.Equal("missing_directory", status.Reasons[0]);
        AssertEx.Equal(3, status.Checks.Count);
        AssertEx.False(status.Reasons is string[], "Runtime preflight reasons should not expose a mutable array.");
        AssertEx.False(status.Checks is ProxyRuntimePreflightCheck[], "Runtime preflight checks should not expose a mutable array.");
        var response = ProxyRuntimePreflightStatusResponseMapper.FromStatus(status);
        AssertEx.False(ReferenceEquals(status.Reasons, response.Reasons), "Runtime preflight API reasons should be transport-owned.");
        AssertEx.False(response.Reasons is string[], "Runtime preflight API reasons should not expose a mutable array.");
        AssertEx.False(response.Checks is List<ProxyRuntimePreflightCheckResponse>, "Runtime preflight API checks should not expose a mutable list.");
        AssertEx.False(response.Checks is ProxyRuntimePreflightCheckResponse[], "Runtime preflight API checks should not expose a mutable array.");
        var directReasons = new List<string>
        {
            "direct_reason"
        };
        var directChecks = new List<ProxyRuntimePreflightCheckResponse>
        {
            response.Checks[0]
        };
        var directResponse = new ProxyRuntimePreflightStatusResponse("degraded", generatedAtUtc, directReasons, directChecks);
        directReasons[0] = "replacement_reason";
        directChecks[0] = directChecks[0] with
        {
            Reason = "replacement_check"
        };
        directReasons.Clear();
        directChecks.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRuntimePreflightStatusResponse("degraded", generatedAtUtc, null!, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRuntimePreflightStatusResponse("degraded", generatedAtUtc, [], null!));
        AssertEx.Equal("direct_reason", directResponse.Reasons[0]);
        AssertEx.Equal("missing_directory", directResponse.Checks[0].Reason);
        AssertEx.False(directResponse.Reasons is string[], "Direct runtime preflight API reasons should not expose a mutable array.");
        AssertEx.False(directResponse.Checks is ProxyRuntimePreflightCheckResponse[], "Direct runtime preflight API checks should not expose a mutable array.");
    }

    public static void RuntimePreflightCheckFactoryBuildsUnsafeAndProbedChecks()
    {
        var logs = new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.Logs, "logs_directory", "logs", Critical: false);
        var config = new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.Config, "config_directory", "config", Critical: true);
        var unsafeCheck = ProxyRuntimePreflightCheckFactory.UnsafePath(logs);
        var probedCheck = ProxyRuntimePreflightCheckFactory.FromProbeResult(config, ProxyRuntimeDirectoryProbeResult.Missing());
        AssertEx.Equal("logs_directory", unsafeCheck.Name);
        AssertEx.Equal("logs", unsafeCheck.RelativePath);
        AssertEx.False(unsafeCheck.Exists);
        AssertEx.Equal(ProxyStatusText.Warning, unsafeCheck.Severity);
        AssertEx.Equal("unsafe_path", unsafeCheck.Reason);
        AssertEx.Equal("config_directory", probedCheck.Name);
        AssertEx.Equal("config", probedCheck.RelativePath);
        AssertEx.False(probedCheck.Exists);
        AssertEx.Equal(ProxyStatusText.Error, probedCheck.Severity);
        AssertEx.Equal("missing_directory", probedCheck.Reason);
    }

    private static MdravaDataDirectoryProvider Provider(string dataDirectory)
    {
        return new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory });
    }

    private sealed class DelegateProbe : IProxyRuntimeDirectoryProbe
    {
        private readonly Func<string, ProxyRuntimeDirectoryProbeResult> _probe;
        public DelegateProbe(Func<string, ProxyRuntimeDirectoryProbeResult> probe)
        {
            _probe = probe;
        }

        public ProxyRuntimeDirectoryProbeResult Probe(string path, bool createIfMissing)
        {
            return _probe(path);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class UnsafeLogsProvider : IMdravaDataDirectoryProvider
    {
        private readonly string _dataDirectory;
        private readonly string _logsDirectory;
        public UnsafeLogsProvider(string dataDirectory, string logsDirectory)
        {
            _dataDirectory = dataDirectory;
            _logsDirectory = logsDirectory;
        }

        public string GetDataDirectory() => _dataDirectory;
        public string GetProxyConfigDirectory() => Path.Combine(_dataDirectory, "config");
        public string GetSitesConfigDirectory() => Path.Combine(_dataDirectory, "config", "sites");
        public string GetProxyOperationalConfigPath() => Path.Combine(_dataDirectory, "config", "proxy.json");
        public string GetLogsDirectory() => _logsDirectory;
        public string GetCertificatesDirectory() => Path.Combine(_dataDirectory, "certs");
        public string GetStateDirectory() => Path.Combine(_dataDirectory, "state");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-preflight-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
