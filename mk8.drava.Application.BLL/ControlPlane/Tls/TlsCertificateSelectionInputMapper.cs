using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Tls;
public static class TlsCertificateSelectionInputMapper
{
    public static TlsCertificateSelectionInput FromSources(IEnumerable<KeyValuePair<string, RuntimeCertificate>> certificates, string? defaultCertificateId, IEnumerable<RuntimeSniCertificateBinding> sniCertificates, string? hostName)
    {
        return new TlsCertificateSelectionInput(certificates, defaultCertificateId, sniCertificates, hostName);
    }
}
