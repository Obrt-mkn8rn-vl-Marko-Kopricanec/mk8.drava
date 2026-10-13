using Certes;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.Acme;

internal static class AcmeAccountKeyReader
{
    public static IKey Read(SecretText configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var pem = configured.RequirePrivateP256Key();
        try { return KeyFactory.FromPem(pem); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or FormatException)
        { throw new InvalidDataException("The configured ACME private account key is invalid.", exception); }
    }
}
