using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Tls;
public static class TlsCertificateSelector
{
    public static X509Certificate2? SelectCertificate(TlsCertificateSelectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!string.IsNullOrWhiteSpace(input.HostName))
        {
            foreach (var binding in input.SniCertificates)
            {
                if (string.Equals(binding.HostName, input.HostName, StringComparison.OrdinalIgnoreCase) && input.Certificates.TryGetValue(binding.CertificateId, out var certificate))
                {
                    return certificate.Certificate;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(input.DefaultCertificateId) && input.Certificates.TryGetValue(input.DefaultCertificateId, out var defaultCertificate))
        {
            return defaultCertificate.Certificate;
        }

        return null;
    }
}
