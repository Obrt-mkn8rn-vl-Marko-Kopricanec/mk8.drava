namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IProxyAcmeCertificateLifecycleStatusSource
{
    IReadOnlyList<AcmeCertificateLifecycleStatus> GetLifecycleStatuses();
}
