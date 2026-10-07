namespace Mk8.Drava.Application.BLL.Registry;

public sealed record GatewayPublicationProof(long Generation, DateTimeOffset ValidUntilUtc, string Domain, int HttpPort, int HttpsPort)
{
    // Current validated serving plans contain exactly the site wildcard and registration name.
    public bool CoversCertificateHost(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var suffix = "." + Domain;
        if (!host.EndsWith(suffix, StringComparison.Ordinal) || host.Length <= suffix.Length) return false;
        return !host.AsSpan(0, host.Length - suffix.Length).Contains('.');
    }
}
