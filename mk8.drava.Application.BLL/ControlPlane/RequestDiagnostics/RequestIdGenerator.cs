using System.Globalization;

namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public sealed class RequestIdGenerator
{
    private readonly IProxyRequestIdMetricsSink _metrics;
    private readonly IProxyRequestIdRuntimeIdentitySource _runtimeIdentitySource;
    private long _nextId;
    public RequestIdGenerator(IProxyRequestIdMetricsSink metrics, IProxyRequestIdRuntimeIdentitySource runtimeIdentitySource)
    {
        _metrics = metrics;
        _runtimeIdentitySource = runtimeIdentitySource;
    }

    public string Create()
    {
        var value = Interlocked.Increment(ref _nextId);
        _metrics.RequestIdGenerated();
        return string.Create(CultureInfo.InvariantCulture, $"mdr-{_runtimeIdentitySource.RuntimeIdentity}-{value:x}");
    }
}
