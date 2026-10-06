using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IAcmeCertificateActivator
{
    void Activate(RuntimeCertificate certificate);
}
