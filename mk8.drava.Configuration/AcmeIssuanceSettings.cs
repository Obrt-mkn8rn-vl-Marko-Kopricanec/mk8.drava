namespace Mk8.Drava.Configuration;

public sealed record AcmeIssuanceSettings
{
    public bool Enabled { get; init; }
    public Uri? DirectoryUrl { get; init; }
    public bool UseStaging { get; init; }
    public string AccountKeyPath { get; init; } = "";
    public string CleanupJournalPath { get; init; } = "";
    public NativeDnsManagementSettings? NativeManagement { get; init; }
    public int NativeDnsTtlSeconds { get; init; } = 300;
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
        RequirePrivatePath(AccountKeyPath);
        var native = string.Equals(dns.Provider, "mk8.dns", StringComparison.Ordinal);
        if (native) ValidateNative(dns);
        else
        {
            RequirePrivatePath(CleanupJournalPath);
            if (NativeManagement is not null) throw new InvalidDataException("Native DNS01 settings cannot accompany another provider.");
        }
        if (!native && string.Equals(AccountKeyPath, CleanupJournalPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Account key and cleanup journal require separate private files.");
        _ = RequireDirectoryUrl();
        if (string.Equals(servingTrust.Mode, "site-ca", StringComparison.Ordinal) || !native && !string.Equals(dns.Provider, "cloudflare", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(AccountKeyPath) || !native && !Path.IsPathFullyQualified(CleanupJournalPath) || !TermsAccepted || ContactEmails.Count is < 1 or > 8)
            throw new InvalidDataException("Automatic public issuance requires an approved HTTPS directory, DNS01 scope, private account key, contacts and accepted terms.");
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

    public Uri RequireDirectoryUrl()
    {
        var directory = DirectoryUrl;
        if (directory is null || !directory.IsAbsoluteUri || !string.Equals(directory.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            directory.Host.Length == 0 || directory.UserInfo.Length != 0 || directory.Query.Length != 0 || directory.Fragment.Length != 0)
        {
            throw new InvalidDataException("Automatic issuance requires an explicitly configured HTTPS directory without credentials, query or fragment.");
        }
        return directory;
    }

    private void ValidateNative(DnsPublicationSettings dns)
    {
        var records = dns.NativeManagement ?? throw new InvalidDataException("Native records profile is absent.");
        var acme = NativeManagement ?? throw new InvalidDataException("Native DNS01 requires a separately provisioned capability profile.");
        acme.Validate(records.Origin, acme: true);
        if (CleanupJournalPath.Length != 0 || CleanupTimeoutSeconds < 20 || NativeDnsTtlSeconds is < 60 or > 3600 ||
            acme.TenantId != records.TenantId || acme.ZoneId != records.ZoneId || !string.Equals(acme.Origin, records.Origin, StringComparison.Ordinal) || !string.Equals(acme.SocketPath, records.SocketPath, StringComparison.Ordinal) ||
            string.Equals(acme.CredentialPath, records.CredentialPath, StringComparison.Ordinal))
            throw new InvalidDataException("Native DNS01 requires the matched zone/socket, a distinct private capability file and the Application durable journal.");
    }

    private static void RequirePrivatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal))
            throw new InvalidDataException("Automatic issuance requires canonical absolute private paths.");
    }
}
