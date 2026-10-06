namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IProxyAcmeStatusSnapshotReader
{
    ProxyAcmeStatusSnapshotReadResult ReadSnapshot();
    IReadOnlyList<AcmeCertificateLifecycleStatus> GetLifecycleStatuses();
}
