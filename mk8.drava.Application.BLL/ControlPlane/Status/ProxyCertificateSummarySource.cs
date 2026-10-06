using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyCertificateSummarySource
{
    public ProxyCertificateSummarySource(IEnumerable<string> ReferencedCertificateIds, IEnumerable<ProxyCertificateValiditySource> LoadedCertificates)
    {
        ArgumentNullException.ThrowIfNull(ReferencedCertificateIds);
        ArgumentNullException.ThrowIfNull(LoadedCertificates);
        this.ReferencedCertificateIds = ProxyStatusList.CopyStrings(ReferencedCertificateIds, nameof(ReferencedCertificateIds));
        this.LoadedCertificates = ProxyStatusList.Copy(LoadedCertificates);
    }

    public IReadOnlyList<string> ReferencedCertificateIds { get; }
    public IReadOnlyList<ProxyCertificateValiditySource> LoadedCertificates { get; }
}
