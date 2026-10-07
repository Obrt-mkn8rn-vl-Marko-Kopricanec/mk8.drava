using System.Security.Cryptography;
using System.Text;
using Certes;
using Mk8.Drava.Application.DAL.Acme;

namespace Mk8.Drava.Application.INF.Acme;

internal static class AcmeAccountKeyStore
{
    public static async ValueTask<IKey> OpenAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            var pem = Encoding.ASCII.GetString(PrivateCertificateFile.Read(path));
            RequirePrivateP256(pem);
            try { return KeyFactory.FromPem(pem); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or FormatException)
            { throw new InvalidDataException("The persisted ACME private account key is invalid.", exception); }
        }
        var created = KeyFactory.NewKey(KeyAlgorithm.ES256);
        await PrivateCertificateFile.WriteNewAsync(path, Encoding.ASCII.GetBytes(created.ToPem()), cancellationToken).ConfigureAwait(false);
        return created;
    }

    private static void RequirePrivateP256(string pem)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            if (!string.Equals(key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value, "1.2.840.10045.3.1.7", StringComparison.Ordinal))
                throw new InvalidDataException("ACME account requires its persisted private P256 key.");
            Span<byte> privateProof = stackalloc byte[512];
            try
            {
                if (!key.TryExportPkcs8PrivateKey(privateProof, out _)) throw new InvalidDataException("ACME account private key exceeds its P256 bound.");
            }
            finally { CryptographicOperations.ZeroMemory(privateProof); }
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        { throw new InvalidDataException("The persisted ACME private account key is invalid.", exception); }
    }
}
