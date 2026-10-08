using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Tls;
public sealed record TlsCertificateSelectionInput
{
    public TlsCertificateSelectionInput(IEnumerable<KeyValuePair<string, RuntimeCertificate>> certificates, string? defaultCertificateId, IEnumerable<RuntimeSniCertificateBinding> sniCertificates, string? hostName)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(sniCertificates);
        Certificates = CopyCertificates(certificates);
        DefaultCertificateId = defaultCertificateId;
        SniCertificates = RuntimeList.Copy(sniCertificates);
        HostName = hostName;
    }

    public IReadOnlyDictionary<string, RuntimeCertificate> Certificates { get; }
    public string? DefaultCertificateId { get; }
    public IReadOnlyList<RuntimeSniCertificateBinding> SniCertificates { get; }
    public string? HostName { get; }

    private static ReadOnlyDictionary<string, RuntimeCertificate> CopyCertificates(IEnumerable<KeyValuePair<string, RuntimeCertificate>> certificates)
    {
        var copy = new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase);
        foreach (var certificate in certificates)
        {
            ArgumentNullException.ThrowIfNull(certificate.Key, nameof(certificates));
            ArgumentNullException.ThrowIfNull(certificate.Value, nameof(certificates));
            copy.Add(certificate.Key, certificate.Value);
        }

        return new ReadOnlyDictionary<string, RuntimeCertificate>(copy);
    }
}
