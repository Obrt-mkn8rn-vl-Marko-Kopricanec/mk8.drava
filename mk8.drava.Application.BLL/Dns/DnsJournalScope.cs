using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.Dns;

public sealed record DnsJournalScope(string Purpose, string Fingerprint)
{
    public void Validate()
    {
        if (Purpose is not ("records" or "acme")) throw new InvalidDataException("Unknown DNS journal purpose.");
        RegistryNames.RequireFingerprint(Fingerprint);
    }
}
