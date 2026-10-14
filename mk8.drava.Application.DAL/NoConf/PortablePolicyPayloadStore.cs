using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.DAL.NoConf;

public sealed class PortablePolicyPayloadStore : IPolicyPayloadStore
{
    private readonly PortableRole<ApplicationBootstrap> _role;
    private readonly string _siteId;

    public PortablePolicyPayloadStore(PortableRole<ApplicationBootstrap> role)
    {
        ArgumentNullException.ThrowIfNull(role);
        _role = role;
        _siteId = role.Configuration.SiteId;
        _ = new PolicyPayloadSnapshot(_siteId, []);
    }

    public ValueTask<PolicyPayloadSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(PortablePolicyArchiveCodec.Read(_role.ReadPolicyDocuments().Archive, _siteId));
    }

    // Repository startup calls this after acquiring its writer lease; the Application lease is caller-owned.
    internal async ValueTask CaptureOwnedAsync(CancellationToken cancellationToken) =>
        _ = await _role.RefreshPolicyDocumentsAsync(cancellationToken).ConfigureAwait(false);

    // Import is limited to a fresh Application role and exactly the approved SQL-live payloads.
    public async ValueTask RestoreAsync(PortablePolicyArchive archive, IReadOnlyList<PolicyPayloadReference> retained, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(retained);
        if (retained.Count > 64) throw new InvalidDataException("Policy restore exceeds retained history bounds.");
        var snapshot = PortablePolicyArchiveCodec.Read(archive, _siteId);
        var payloads = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in retained)
        {
            if (reference is null) throw new InvalidDataException("Policy restore has an absent approved reference.");
            payloads.TryAdd(reference.Digest, snapshot.Resolve(reference));
        }
        if (payloads.Count != snapshot.References.Count) throw new InvalidDataException("Policy restore contains unapproved orphan payloads.");
        var prior = _role.ReadPolicyDocuments().Archive;
        if (!prior.IsEmpty) throw new InvalidOperationException("Policy restore requires a fresh Application archive.");
        await _role.WritePolicyArchiveAsync(prior, PortablePolicyArchiveCodec.Encode(new PolicyPayloadSnapshot(_siteId, payloads)), cancellationToken).ConfigureAwait(false);
    }

    // The caller must retain all SQL-live references and serialize staging under its repository gate.
    public async ValueTask<PolicyPayloadReference> StageAsync(string canonicalJson, IReadOnlyList<PolicyPayloadReference> retained, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalJson);
        ArgumentNullException.ThrowIfNull(retained);
        if (retained.Count > 64) throw new InvalidDataException("Policy staging must retain at most 64 committed references.");
        cancellationToken.ThrowIfCancellationRequested();
        var reference = PolicyPayloadSnapshot.Describe(canonicalJson);
        var documents = _role.ReadPolicyDocuments();
        var snapshot = PortablePolicyArchiveCodec.Read(documents.Archive, _siteId);
        var payloads = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var live in retained)
        {
            if (live is null) throw new InvalidDataException("Retained policy references cannot contain null entries.");
            payloads.TryAdd(live.Digest, snapshot.Resolve(live));
        }
        if (snapshot.References.Any(existing => string.Equals(existing.Digest, reference.Digest, StringComparison.Ordinal))
            && !string.Equals(snapshot.Resolve(reference), canonicalJson, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Policy archive digest collision cannot replace existing content.");
        }
        payloads[reference.Digest] = canonicalJson;
        var replacement = PortablePolicyArchiveCodec.Encode(new PolicyPayloadSnapshot(_siteId, payloads));
        await _role.WritePolicyArchiveAsync(documents.Archive, replacement, cancellationToken).ConfigureAwait(false);
        return reference;
    }
}
