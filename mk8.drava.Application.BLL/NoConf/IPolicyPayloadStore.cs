namespace Mk8.Drava.Application.BLL.NoConf;

// The repository stages under its writer/fence gate before accepting a payload reference.
public interface IPolicyPayloadStore
{
    ValueTask<PolicyPayloadSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
    ValueTask<PolicyPayloadReference> StageAsync(string canonicalJson, IReadOnlyList<PolicyPayloadReference> retained, CancellationToken cancellationToken);
}
