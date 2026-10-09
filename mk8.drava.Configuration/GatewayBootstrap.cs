namespace Mk8.Drava.Configuration;

public sealed record GatewayBootstrap
{
    public int SchemaVersion { get; init; } = 1;
    public string GatewayId { get; init; } = "local";
    public string SiteId { get; init; } = "";
    public string BindAddress { get; init; } = "127.0.0.1";
    public int HttpPort { get; init; } = 80;
    public int HttpsPort { get; init; } = 443;
    public int RegistrationPort { get; init; } = 9443;
    public int ManagementPort { get; init; }
    public bool DiscoveryEnabled { get; init; } = true;
    public string StateDirectory { get; init; } = "";
    public IpcEndpoint Application { get; init; } = new();
    public GatewayPlanSettings Plan { get; init; } = new();
    public ServingTrustSettings ServingTrust { get; init; } = new();
    public int MaxConcurrentExchanges { get; init; } = 256;
    public int MaxHeaderBytes { get; init; } = 32 * 1024;
    public long MaxRequestBodyBytes { get; init; } = 100L * 1024 * 1024;
    public IReadOnlyList<GatewayRequestBodyLimit> RequestBodyLimits { get; init; } = [];
    public int FrameBytes { get; init; } = 32 * 1024;
    public int StreamWindowFrames { get; init; } = 4;
    public string EnrollmentRootFingerprint { get; init; } = "";

    public void Validate()
    {
        if (SchemaVersion != 1 || GatewayId.Length is < 1 or > 128 || SiteId.Length is < 1 or > 128) throw new InvalidDataException("Invalid Gateway bootstrap identity or version.");
        if (!System.Net.IPAddress.TryParse(BindAddress, out _)) throw new InvalidDataException("Gateway bind address must be an IP literal.");
        if (HttpPort is < 0 or > 65535 || HttpsPort is < 0 or > 65535 || RegistrationPort is < 1 or > 65535 || (HttpPort == 0 && HttpsPort == 0)) throw new InvalidDataException("Invalid Gateway listener ports.");
        if (ManagementPort is < 0 or > 65535 || (ManagementPort > 0 && EnrollmentRootFingerprint.Length == 0)) throw new InvalidDataException("Management requires an enrolled TLS listener.");
        if (new[] { HttpPort, HttpsPort, RegistrationPort, ManagementPort }.Where(static port => port != 0).Distinct().Count() != new[] { HttpPort, HttpsPort, RegistrationPort, ManagementPort }.Count(static port => port != 0)) throw new InvalidDataException("Gateway listener ports must be distinct.");
        if (MaxConcurrentExchanges is < 1 or > 4096 || MaxHeaderBytes is < 1024 or > 65536 || MaxRequestBodyBytes is < 0 or > 1L * 1024 * 1024 * 1024 * 1024 || FrameBytes is < 1024 or > 32768 || StreamWindowFrames is < 1 or > 8) throw new InvalidDataException("Invalid Gateway resource bounds.");
        ValidateBodyLimits();
        if (!Path.IsPathFullyQualified(StateDirectory)) throw new InvalidDataException("Gateway state directory must be absolute.");
        Application.Validate(); Plan.Validate(); ServingTrust.Validate();
        if (EnrollmentRootFingerprint.Length != 0)
        {
            if (EnrollmentRootFingerprint.Length != 64) throw new InvalidDataException("Invalid enrollment root fingerprint.");
            foreach (var character in EnrollmentRootFingerprint)
                if (character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')) throw new InvalidDataException("Invalid enrollment root fingerprint encoding.");
        }
    }

    public long ResolveRequestBodyLimit(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        foreach (var limit in RequestBodyLimits)
            if (string.Equals(limit.Host, host, StringComparison.OrdinalIgnoreCase)) return limit.MaxRequestBodyBytes;
        return MaxRequestBodyBytes;
    }

    private void ValidateBodyLimits()
    {
        if (RequestBodyLimits is null || RequestBodyLimits.Count > 64)
            throw new InvalidDataException("Gateway body limits require a bounded host list.");
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var limit in RequestBodyLimits)
        {
            if (limit is null) throw new InvalidDataException("A Gateway body limit is missing.");
            limit.Validate();
            if (!hosts.Add(limit.Host)) throw new InvalidDataException("Gateway body limit hosts must be unique.");
        }
    }
}
