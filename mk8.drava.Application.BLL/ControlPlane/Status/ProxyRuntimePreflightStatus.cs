namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyRuntimePreflightStatus
{
    private ProxyRuntimePreflightStatus(string state, DateTimeOffset? generatedAtUtc, IEnumerable<string> reasons, IEnumerable<ProxyRuntimePreflightCheck> checks)
    {
        State = state;
        GeneratedAtUtc = generatedAtUtc;
        Reasons = ProxyStatusList.Copy(reasons);
        Checks = ProxyStatusList.Copy(checks);
    }

    public string State { get; }
    public DateTimeOffset? GeneratedAtUtc { get; }
    public IReadOnlyList<string> Reasons { get; }
    public IReadOnlyList<ProxyRuntimePreflightCheck> Checks { get; }
    public static ProxyRuntimePreflightStatus Unknown { get; } = new(ProxyStatusText.Unknown, generatedAtUtc: null, reasons: [], checks: []);

    public static ProxyRuntimePreflightStatus Completed(DateTimeOffset generatedAtUtc, IEnumerable<ProxyRuntimePreflightCheck> checks, int maxReasons)
    {
        var ownedChecks = ProxyStatusList.Copy(checks);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReasons);
        var failed = ownedChecks.Any(static check => string.Equals(check.Severity, ProxyStatusText.Error, StringComparison.OrdinalIgnoreCase));
        var degraded = ownedChecks.Any(static check => string.Equals(check.Severity, ProxyStatusText.Warning, StringComparison.OrdinalIgnoreCase));
        var state = failed ? ProxyStatusText.Failed : degraded ? ProxyStatusText.Degraded : ProxyStatusText.Healthy;
        var reasons = ownedChecks.Where(static check => !string.Equals(check.Reason, ProxyStatusText.Ok, StringComparison.OrdinalIgnoreCase)).Select(static check => check.Reason).Where(static reason => !string.IsNullOrWhiteSpace(reason)).Distinct(StringComparer.OrdinalIgnoreCase).Take(maxReasons);
        return new ProxyRuntimePreflightStatus(state, generatedAtUtc, reasons, ownedChecks);
    }
}
