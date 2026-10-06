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
    public string StateDirectory { get; init; } = "";
    public IpcEndpoint Application { get; init; } = new();
    public int MaxConcurrentExchanges { get; init; } = 256;
    public int MaxHeaderBytes { get; init; } = 32 * 1024;
    public long MaxRequestBodyBytes { get; init; } = 100L * 1024 * 1024;
    public int FrameBytes { get; init; } = 32 * 1024;
    public int StreamWindowFrames { get; init; } = 4;

    public void Validate()
    {
        if (SchemaVersion != 1 || GatewayId.Length is < 1 or > 128 || SiteId.Length is < 1 or > 128) throw new InvalidDataException("Invalid Gateway bootstrap identity or version.");
        if (!System.Net.IPAddress.TryParse(BindAddress, out _)) throw new InvalidDataException("Gateway bind address must be an IP literal.");
        if (HttpPort is < 0 or > 65535 || HttpsPort is < 0 or > 65535 || RegistrationPort is < 1 or > 65535 || (HttpPort == 0 && HttpsPort == 0)) throw new InvalidDataException("Invalid Gateway listener ports.");
        if (new[] { HttpPort, HttpsPort, RegistrationPort }.Where(static port => port != 0).Distinct().Count() != new[] { HttpPort, HttpsPort, RegistrationPort }.Count(static port => port != 0)) throw new InvalidDataException("Gateway serving and registration ports must be distinct.");
        if (MaxConcurrentExchanges is < 1 or > 4096 || MaxHeaderBytes is < 1024 or > 65536 || MaxRequestBodyBytes < 0 || FrameBytes is < 1024 or > 32768 || StreamWindowFrames is < 1 or > 8) throw new InvalidDataException("Invalid Gateway resource bounds.");
        if (!Path.IsPathFullyQualified(StateDirectory)) throw new InvalidDataException("Gateway state directory must be absolute.");
        Application.Validate();
    }
}
