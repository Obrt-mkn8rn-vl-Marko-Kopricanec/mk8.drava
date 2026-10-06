namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed class ProxyAcmeCertificateLifecycleStatusSource : IProxyAcmeCertificateLifecycleStatusSource
{
    private readonly AcmeCertificateStatusStore _statusStore;
    public ProxyAcmeCertificateLifecycleStatusSource(AcmeCertificateStatusStore statusStore)
    {
        _statusStore = statusStore;
    }

    public IReadOnlyList<AcmeCertificateLifecycleStatus> GetLifecycleStatuses()
    {
        return _statusStore.Snapshot();
    }
}
