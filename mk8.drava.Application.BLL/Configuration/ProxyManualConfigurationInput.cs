using System.Collections.Frozen;

namespace Mk8.Drava.Application.BLL.Configuration;

// Text and PFX values are immutable owned inputs; parsing never consults file or environment locators.
public sealed class ProxyManualConfigurationInput
{
    private readonly FrozenDictionary<string, ProxyManualCertificateInput> _certificates;

    public ProxyManualConfigurationInput(string operationalDocument, IEnumerable<ProxyManualSiteInput> sites,
        IEnumerable<ProxyManualCertificateInput> certificates, RuntimeAdminSecurityOptions administrator)
    {
        ArgumentNullException.ThrowIfNull(operationalDocument);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(administrator);
        OperationalDocument = operationalDocument;
        var capturedSites = new List<ProxyManualSiteInput>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in sites)
        {
            if (site is null || capturedSites.Count >= 128 || !names.Add(site.Name)) throw new InvalidDataException("Protected manual sites are duplicate, absent or excessive.");
            capturedSites.Add(site);
        }
        Sites = capturedSites.AsReadOnly();
        var capturedCertificates = new Dictionary<string, ProxyManualCertificateInput>(StringComparer.OrdinalIgnoreCase);
        foreach (var certificate in certificates)
        {
            if (certificate is null || capturedCertificates.Count >= 64 || !capturedCertificates.TryAdd(certificate.Id, certificate))
                throw new InvalidDataException("Protected manual certificates are duplicate, absent or excessive.");
        }
        _certificates = capturedCertificates.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        Certificates = Array.AsReadOnly(capturedCertificates.Values.ToArray());
        Administrator = administrator;
    }

    public string OperationalDocument { get; }
    public IReadOnlyList<ProxyManualSiteInput> Sites { get; }
    public IReadOnlyList<ProxyManualCertificateInput> Certificates { get; }
    public RuntimeAdminSecurityOptions Administrator { get; }
    public ProxyManualCertificateInput? FindCertificate(string id) => _certificates.GetValueOrDefault(id);
    public ProxyManualCertificateInput ResolveCertificate(string id) => _certificates.TryGetValue(id, out var material)
        ? material : throw new InvalidDataException("Protected manual certificate material is absent.");
    public override string ToString() => "[redacted]";

    internal static void RequireName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is < 1 or > 128 || name[0] == '.'
            || name.Any(static value => !char.IsAsciiLetterOrDigit(value) && value is not ('-' or '_' or '.')))
        {
            throw new InvalidDataException("Protected manual inputs require portable leaf names.");
        }
    }
}

