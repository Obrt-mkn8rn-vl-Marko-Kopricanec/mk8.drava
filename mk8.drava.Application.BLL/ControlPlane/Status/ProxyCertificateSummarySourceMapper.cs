using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyCertificateSummarySourceMapper
{
    public static ProxyCertificateSummarySource FromSources(IEnumerable<RuntimeListener> listeners, IEnumerable<RuntimeCertificate> certificates)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(certificates);
        List<string> referenced = [];
        foreach (var listener in listeners)
        {
            ArgumentNullException.ThrowIfNull(listener, nameof(listeners));
            if (!string.IsNullOrWhiteSpace(listener.DefaultCertificateId))
            {
                referenced.Add(listener.DefaultCertificateId);
            }

            foreach (var binding in listener.SniCertificates)
            {
                ArgumentNullException.ThrowIfNull(binding, nameof(listeners));
                referenced.Add(binding.CertificateId);
            }
        }

        return new ProxyCertificateSummarySource(referenced, certificates.Select(ToValiditySource).ToArray());
    }

    private static ProxyCertificateValiditySource ToValiditySource(RuntimeCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return new ProxyCertificateValiditySource(certificate.Id, certificate.Certificate.NotBefore, certificate.Certificate.NotAfter);
    }
}
