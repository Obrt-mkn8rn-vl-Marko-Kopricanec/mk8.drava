namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;

public interface IAcmeCertificateStatusPersistence
{
    ValueTask<IReadOnlyList<AcmeCertificateLifecycleStatus>> ReadAsync(CancellationToken cancellationToken);
    ValueTask UpsertAsync(AcmeCertificateLifecycleStatus status, CancellationToken cancellationToken);
}
