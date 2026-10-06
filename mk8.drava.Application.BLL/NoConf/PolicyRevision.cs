using System.Security.Cryptography;
using System.Text;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.NoConf;

// Accepted desired configuration. Eligibility proofs and live counters are deliberately separate.
public sealed record PolicyRevision
{
    public PolicyRevision(long revision, string canonicalJson, DateTimeOffset acceptedAtUtc, string actor, string source)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        ArgumentNullException.ThrowIfNull(canonicalJson);
        RegistryNames.RequireLabel(actor);
        if (source is not ("file" or "control")) throw new InvalidDataException("Unknown policy authority.");
        var bytes = Encoding.UTF8.GetBytes(canonicalJson);
        if (bytes.Length is < 2 or > 256 * 1024) throw new InvalidDataException("Policy exceeds its durable bound.");
        Revision = revision;
        CanonicalJson = canonicalJson;
        AcceptedAtUtc = acceptedAtUtc;
        Actor = actor;
        Source = source;
        Digest = Convert.ToHexString(SHA256.HashData(bytes));
    }

    public long Revision { get; }
    public string CanonicalJson { get; }
    public DateTimeOffset AcceptedAtUtc { get; }
    public string Actor { get; }
    public string Source { get; }
    public string Digest { get; }
}
