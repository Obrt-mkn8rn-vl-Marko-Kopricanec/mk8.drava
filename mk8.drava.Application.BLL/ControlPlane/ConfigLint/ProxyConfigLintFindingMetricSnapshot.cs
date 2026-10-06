namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ProxyConfigLintFindingMetricSnapshot
{
    public ProxyConfigLintFindingMetricSnapshot(string severity, string code, long count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Severity = severity;
        Code = code;
        Count = count;
    }

    public string Severity { get; }
    public string Code { get; }
    public long Count { get; }
}
