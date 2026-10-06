namespace Mk8.Drava.Configuration;

public sealed record IpcEndpoint
{
    public string UnixSocketPath { get; init; } = "";
    public string NamedPipeName { get; init; } = "";
    public string HttpsAddress { get; init; } = "";
    public string ClientCertificatePath { get; init; } = "";
    public string TrustedCertificatePath { get; init; } = "";
    public string IdentityTokenPath { get; init; } = "";

    public void Validate()
    {
        var choices = (UnixSocketPath.Length > 0 ? 1 : 0) + (NamedPipeName.Length > 0 ? 1 : 0) + (HttpsAddress.Length > 0 ? 1 : 0);
        if (choices != 1) throw new InvalidDataException("Select exactly one private IPC endpoint.");
        if (UnixSocketPath.Length > 0 && (!Path.IsPathFullyQualified(UnixSocketPath) || UnixSocketPath.Length > 100)) throw new InvalidDataException("The Unix socket path must be absolute and fit the platform socket limit.");
        if (NamedPipeName.Length > 0 && (NamedPipeName.Length > 128 || NamedPipeName.Any(static value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))) throw new InvalidDataException("Invalid private pipe name.");
        if (HttpsAddress.Length > 0)
        {
            if (!Uri.TryCreate(HttpsAddress, UriKind.Absolute, out var address) || !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || !string.Equals(address.AbsolutePath, "/", StringComparison.Ordinal) || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0) throw new InvalidDataException("Remote Application transport requires an HTTPS authority.");
            if (ClientCertificatePath.Length == 0 || TrustedCertificatePath.Length == 0) throw new InvalidDataException("Remote Application transport requires mutual TLS enrollment.");
        }
        if (IdentityTokenPath.Length == 0 || !Path.IsPathFullyQualified(IdentityTokenPath)) throw new InvalidDataException("Private transport requires a scoped identity token file.");
    }
}
