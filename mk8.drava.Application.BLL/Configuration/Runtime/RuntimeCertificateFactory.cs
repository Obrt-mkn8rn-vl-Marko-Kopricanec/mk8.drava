using System.Security.Cryptography.X509Certificates;

namespace Mk8.Drava.Application.BLL.Configuration;
public static class RuntimeCertificateFactory
{
    public const string ManualPfxSource = "manualPfx";
    public const string AcmeSource = "acme";
    public const string PfxFormat = "pfx";
    public static RuntimeCertificate ManualPfx(string id, string path, bool hasConfiguredPassword, X509Certificate2 certificate)
    {
        return new RuntimeCertificate(id, path, PfxFormat, hasConfiguredPassword, certificate, ManualPfxSource, []);
    }

    public static RuntimeCertificate Acme(string id, X509Certificate2 certificate, IReadOnlyList<string> domains)
    {
        return new RuntimeCertificate(id, $"acme://{id}", PfxFormat, HasConfiguredPassword: false, certificate, AcmeSource, domains);
    }
}
