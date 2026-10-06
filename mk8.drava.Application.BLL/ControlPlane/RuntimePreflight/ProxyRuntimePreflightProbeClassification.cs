using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
public sealed record ProxyRuntimePreflightProbeClassification
{
    public ProxyRuntimePreflightProbeClassification(string Severity, string Reason)
    {
        ProxyStatusFacts.RequireText(Severity, nameof(Severity));
        ProxyStatusFacts.RequireText(Reason, nameof(Reason));
        this.Severity = Severity;
        this.Reason = Reason;
    }

    public string Severity { get; }
    public string Reason { get; }
}
