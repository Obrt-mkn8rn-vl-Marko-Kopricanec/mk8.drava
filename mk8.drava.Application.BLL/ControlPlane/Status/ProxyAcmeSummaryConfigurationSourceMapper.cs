using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyAcmeSummaryConfigurationSourceMapper
{
    public static ProxyAcmeSummaryConfigurationSource FromSource(RuntimeAcmeOptions acme)
    {
        ArgumentNullException.ThrowIfNull(acme);
        return new ProxyAcmeSummaryConfigurationSource(acme.Enabled, acme.Certificates.Count(static certificate => certificate.Enabled));
    }
}
