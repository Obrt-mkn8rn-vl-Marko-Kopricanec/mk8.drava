namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;

public interface IAcmeDns01CleanupJournal
{
    IReadOnlyList<AcmeDns01CleanupEntry> Read();
    ValueTask PutAsync(AcmeDns01CleanupEntry entry, CancellationToken cancellationToken);
    ValueTask RemoveAsync(string operationId, CancellationToken cancellationToken);
}
