namespace Mk8.Drava.Configuration;

public sealed record AcmeIssuanceSettings
{
    public bool Enabled { get; init; }
    public Uri DirectoryUrl { get; init; } = new("https://acme-v02.api.letsencrypt.org/directory");
    public string AccountKeyPath { get; init; } = "";
    public string CleanupJournalPath { get; init; } = "";
    public IReadOnlyList<string> ContactEmails { get; init; } = [];
    public bool TermsAccepted { get; init; }
    public string PinnedServingRootPath { get; init; } = "";
    public int RequestTimeoutSeconds { get; init; } = 5;
    public int OperationTimeoutSeconds { get; init; } = 300;
    public int PollIntervalSeconds { get; init; } = 2;
    public int CleanupTimeoutSeconds { get; init; } = 15;
    public int CheckIntervalSeconds { get; init; } = 60;
    public int RetryAfterSeconds { get; init; } = 300;

    public void Validate(ServingTrustSettings servingTrust, DnsPublicationSettings dns)
    {
        ArgumentNullException.ThrowIfNull(servingTrust); ArgumentNullException.ThrowIfNull(dns);
        if (!Enabled) return;
        servingTrust.Validate();
        if (ContactEmails is null) throw new InvalidDataException("Automatic issuance requires owner contact email addresses.");
        RequirePrivatePath(AccountKeyPath); RequirePrivatePath(CleanupJournalPath);
        if (string.Equals(AccountKeyPath, CleanupJournalPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Account key and cleanup journal require separate private files.");
        var directory = DirectoryUrl;
        if (string.Equals(servingTrust.Mode, "site-ca", StringComparison.Ordinal) || !string.Equals(dns.Provider, "cloudflare", StringComparison.Ordinal) ||
            directory is null || !directory.IsAbsoluteUri || !string.Equals(directory.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            directory.UserInfo.Length != 0 || directory.Query.Length != 0 || directory.Fragment.Length != 0 ||
            !Path.IsPathFullyQualified(AccountKeyPath) || !Path.IsPathFullyQualified(CleanupJournalPath) || !TermsAccepted || ContactEmails.Count is < 1 or > 8)
            throw new InvalidDataException("Automatic public issuance requires an approved HTTPS directory, Cloudflare scope, private account key, contacts and accepted terms.");
        if (string.Equals(servingTrust.Mode, "pinned", StringComparison.Ordinal) ? !Path.IsPathFullyQualified(PinnedServingRootPath) : PinnedServingRootPath.Length != 0)
            throw new InvalidDataException("Pinned public issuance requires an independently owner-approved root file; system serving trust uses the host root store.");
        foreach (var email in ContactEmails)
        {
            if (email is null || email.Length is < 3 or > 254 || !email.Contains('@', StringComparison.Ordinal) || email.Any(static c => !char.IsAscii(c) || char.IsWhiteSpace(c) || char.IsControl(c)))
                throw new InvalidDataException("Automatic issuance requires bounded owner contact email addresses.");
        }
        if (RequestTimeoutSeconds is < 1 or > 30 || OperationTimeoutSeconds is < 30 or > 600 || PollIntervalSeconds is < 1 or > 30 ||
            CleanupTimeoutSeconds is < 1 or > 30 || CheckIntervalSeconds is < 10 or > 3600 || RetryAfterSeconds is < 30 or > 86400)
            throw new InvalidDataException("Automatic public issuance timing exceeds supported bounds.");
    }

    private static void RequirePrivatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal))
            throw new InvalidDataException("Automatic issuance requires canonical absolute private paths.");
    }
}
