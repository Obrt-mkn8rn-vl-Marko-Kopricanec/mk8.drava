using Mk8.Drava.Application.BLL.ControlPlane.Status;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
public sealed class ProxyRuntimePreflightService : IProxyStatusRuntimePreflightSource
{
    private const int MaxReasons = 12;
    private readonly IMdravaDataDirectoryProvider _dataDirectoryProvider;
    private readonly IProxyDataDirectoryPathSafety _pathSafety;
    private readonly IProxyRuntimeDirectoryProbe _directoryProbe;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private ProxyRuntimePreflightStatus _lastStatus = ProxyRuntimePreflightStatus.Unknown;
    public ProxyRuntimePreflightService(IMdravaDataDirectoryProvider dataDirectoryProvider, IProxyDataDirectoryPathSafety pathSafety, IProxyRuntimeDirectoryProbe directoryProbe, TimeProvider timeProvider)
    {
        _dataDirectoryProvider = dataDirectoryProvider;
        _pathSafety = pathSafety;
        _directoryProbe = directoryProbe;
        _timeProvider = timeProvider;
    }

    public ProxyRuntimePreflightStatus LastStatus
    {
        get
        {
            lock (_gate)
            {
                return _lastStatus;
            }
        }
    }

    public ProxyRuntimePreflightStatus ReadRuntimePreflight()
    {
        return LastStatus;
    }

    public ProxyRuntimePreflightStatus RunStartupChecks()
    {
        var status = Build(createMissingOwnedDirectories: true);
        lock (_gate)
        {
            _lastStatus = status;
        }

        return status;
    }

    public ProxyRuntimePreflightStatus Inspect()
    {
        return Build(createMissingOwnedDirectories: false);
    }

    private ProxyRuntimePreflightStatus Build(bool createMissingOwnedDirectories)
    {
        var generatedAtUtc = _timeProvider.GetUtcNow();
        var dataDirectory = _dataDirectoryProvider.GetDataDirectory();
        List<ProxyRuntimePreflightCheck> checks = [];
        foreach (var requirement in ProxyRuntimePreflightDirectoryPolicy.ExpectedDirectories())
        {
            checks.Add(Check(requirement, ResolveDirectoryPath(requirement.Kind), createMissingOwnedDirectories));
        }

        return ProxyRuntimePreflightStatusBuilder.Build(generatedAtUtc, checks, MaxReasons);
        ProxyRuntimePreflightCheck Check(ProxyRuntimePreflightDirectoryRequirement requirement, string path, bool createMissing)
        {
            if (requirement.Kind != ProxyRuntimePreflightDirectoryKind.Data && _pathSafety.GetSafeRelativePath(dataDirectory, path)is not ProxySafeRelativePathResult.SafeResult)
            {
                return ProxyRuntimePreflightCheckFactory.UnsafePath(requirement);
            }

            var result = _directoryProbe.Probe(path, createMissing);
            return ProxyRuntimePreflightCheckFactory.FromProbeResult(requirement, result);
        }
    }

    private string ResolveDirectoryPath(ProxyRuntimePreflightDirectoryKind kind)
    {
        return kind switch
        {
            ProxyRuntimePreflightDirectoryKind.Data => _dataDirectoryProvider.GetDataDirectory(),
            ProxyRuntimePreflightDirectoryKind.Config => _dataDirectoryProvider.GetProxyConfigDirectory(),
            ProxyRuntimePreflightDirectoryKind.Sites => _dataDirectoryProvider.GetSitesConfigDirectory(),
            ProxyRuntimePreflightDirectoryKind.Logs => _dataDirectoryProvider.GetLogsDirectory(),
            ProxyRuntimePreflightDirectoryKind.Certificates => _dataDirectoryProvider.GetCertificatesDirectory(),
            ProxyRuntimePreflightDirectoryKind.State => _dataDirectoryProvider.GetStateDirectory(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown runtime preflight directory kind.")};
    }
}
