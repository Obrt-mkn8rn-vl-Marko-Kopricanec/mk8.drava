namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeSniCertificateBindingProjection
{
    public RuntimeSniCertificateBindingProjection(string HostName, string CertificateId)
    {
        RuntimeSniCertificateFacts.Validate(HostName, CertificateId);
        this.HostName = HostName;
        this.CertificateId = CertificateId;
    }

    public string HostName { get; }
    public string CertificateId { get; }
}
