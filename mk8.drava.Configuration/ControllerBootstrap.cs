namespace Mk8.Drava.Configuration;

public sealed record ControllerBootstrap
{
    public string Domain { get; init; } = "";
    public string CertificateAuthorityPath { get; init; } = "";
    public string EnrollmentRootFingerprint { get; init; } = "";
    public int RegistrationPort { get; init; } = 9443;
    public IReadOnlyList<string> PublicAddresses { get; init; } = [];
    public string DnsServerAddress { get; init; } = "";
    public int DnsServerPort { get; init; } = 53;
    public DnsPublicationSettings DnsPublication { get; init; } = new();
    public RelayLimits Relay { get; init; } = new();
    public RegistrationSettings Registration { get; init; } = new();
    public ServingPlanSettings ServingPlan { get; init; } = new();
    public ServingTrustSettings ServingTrust { get; init; } = new();
    public string ServingCertificatePath { get; init; } = "";
    public AcmeIssuanceSettings Acme { get; init; } = new();

    public void Validate()
    {
        Relay.Validate(); Registration.Validate(); ServingPlan.Validate(); ServingTrust.Validate();
        if (string.Equals(ServingTrust.Mode, "site-ca", StringComparison.Ordinal) ? ServingCertificatePath.Length != 0 : !Path.IsPathFullyQualified(ServingCertificatePath))
            throw new InvalidDataException("Public serving trust requires an absolute protected certificate path; site CA mode issues its own material.");
        if (Domain.Length is < 3 or > 189 || !Domain.Contains('.', StringComparison.Ordinal)) throw new InvalidDataException("Site requires a fully qualified domain with room for its service label.");
        foreach (var label in Domain.Split('.'))
        {
            if (label.Length is < 1 or > 63 || label.StartsWith('-') || label.EndsWith('-')) throw new InvalidDataException("Invalid site domain.");
            foreach (var character in label)
                if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-') throw new InvalidDataException("Site domain requires canonical ASCII labels.");
        }
        DnsPublication.Validate(Domain);
        Acme.Validate(ServingTrust, DnsPublication);
        if (Acme.Enabled)
        {
            var paths = new[] { CertificateAuthorityPath, ServingCertificatePath, Acme.AccountKeyPath, Acme.CleanupJournalPath, Acme.PinnedServingRootPath };
            var unique = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var path in paths)
                if (path.Length != 0 && !unique.Add(Path.GetFullPath(path))) throw new InvalidDataException("Automatic issuance material and private enrollment require distinct files.");
        }
        if (!Path.IsPathFullyQualified(CertificateAuthorityPath)) throw new InvalidDataException("Site issuer path must be absolute.");
        if (EnrollmentRootFingerprint.Length != 64) throw new InvalidDataException("Site issuer requires its enrolled SHA-256 fingerprint.");
        foreach (var character in EnrollmentRootFingerprint)
            if (character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')) throw new InvalidDataException("Invalid issuer fingerprint.");
        if (RegistrationPort is < 1 or > 65535) throw new InvalidDataException("Invalid registration listener port.");
        if (PublicAddresses.Count > 64) throw new InvalidDataException("Too many public addresses.");
        foreach (var address in PublicAddresses)
            if (!System.Net.IPAddress.TryParse(address, out var parsed) || parsed.Equals(System.Net.IPAddress.Any) || parsed.Equals(System.Net.IPAddress.IPv6Any))
                throw new InvalidDataException("Public addresses require concrete Gateway endpoint literals.");
        if (DnsServerPort is < 1 or > 65535 || (DnsServerAddress.Length > 0 && !System.Net.IPAddress.TryParse(DnsServerAddress, out _)))
            throw new InvalidDataException("DNS verification requires an approved resolver literal and port.");
    }
}
