namespace Mk8.Drava.Application.BLL.Registry;

public interface IRegistryRepository
{
    ValueTask<RegistryState> ReadAsync(CancellationToken cancellationToken);
    ValueTask CommitAsync(long expectedRevision, RegistryState replacement, RegistryAudit audit, CancellationToken cancellationToken);
}
