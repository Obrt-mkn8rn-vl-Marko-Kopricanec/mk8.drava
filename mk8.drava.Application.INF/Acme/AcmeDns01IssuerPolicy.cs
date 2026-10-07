using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.Acme;

internal sealed record AcmeDns01IssuerPolicy
{
    public required string SiteDomain { get; init; }
    public required Uri Directory { get; init; }
    public required string AccountKeyPath { get; init; }
    public IReadOnlyList<string> ContactEmails { get; init; } = [];
    public bool TermsAccepted { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public void Validate()
    {
        new DnsPublicationSettings().Validate(SiteDomain);
        if (!Directory.IsAbsoluteUri || !string.Equals(Directory.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            Directory.UserInfo.Length != 0 || Directory.Query.Length != 0 || Directory.Fragment.Length != 0 ||
            !Path.IsPathFullyQualified(AccountKeyPath) || !TermsAccepted || ContactEmails.Count is < 1 or > 8)
            throw new InvalidDataException("ACME requires owner-approved directory, private account key, contacts and accepted terms.");
        foreach (var email in ContactEmails)
            if (email.Length is < 3 or > 254 || !email.Contains('@', StringComparison.Ordinal) || email.Any(static c => !char.IsAscii(c) || char.IsWhiteSpace(c) || char.IsControl(c)))
                throw new InvalidDataException("ACME requires bounded owner contact email addresses.");
        if (RequestTimeout < TimeSpan.FromSeconds(1) || RequestTimeout > TimeSpan.FromSeconds(30) ||
            OperationTimeout < TimeSpan.FromSeconds(1) || OperationTimeout > TimeSpan.FromMinutes(10) ||
            PollInterval < TimeSpan.FromMilliseconds(100) || PollInterval > TimeSpan.FromSeconds(30) ||
            CleanupTimeout < TimeSpan.FromSeconds(1) || CleanupTimeout > TimeSpan.FromSeconds(30))
            throw new InvalidDataException("ACME timing exceeds supported operation bounds.");
    }
}
