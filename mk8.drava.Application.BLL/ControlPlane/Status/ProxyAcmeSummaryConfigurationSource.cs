using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyAcmeSummaryConfigurationSource
{
    public ProxyAcmeSummaryConfigurationSource(bool Enabled, int ConfiguredCertificates)
    {
        ProxyStatusFacts.RequireNonNegative(ConfiguredCertificates, nameof(ConfiguredCertificates));
        this.Enabled = Enabled;
        this.ConfiguredCertificates = ConfiguredCertificates;
    }

    public bool Enabled { get; }
    public int ConfiguredCertificates { get; }
}
