using System.Security.Cryptography;
using Google.Protobuf;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Protocol;

public static class PresentationPlanDigest
{
    public static byte[] Compute(PresentationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var content = plan.Clone();
        content.ContentSha256 = ByteString.Empty;
        return SHA256.HashData(content.ToByteArray());
    }

    public static bool Verify(PresentationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ContentSha256.Length == 32 && CryptographicOperations.FixedTimeEquals(Compute(plan), plan.ContentSha256.Span);
    }
}
