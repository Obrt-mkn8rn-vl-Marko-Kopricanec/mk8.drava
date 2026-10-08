using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IAcmeCertificateActivator
{
    void Activate(RuntimeCertificate certificate);
    ValueTask ActivateAsync(RuntimeCertificate certificate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Activate(certificate);
        return ValueTask.CompletedTask;
    }
}
