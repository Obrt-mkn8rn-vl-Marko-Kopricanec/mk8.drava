namespace Mk8.Drava.Configuration;

public sealed record ApplicationBootstrap
{
    public int SchemaVersion { get; init; } = 1;
    public string SiteId { get; init; } = "";
    public string NodeId { get; init; } = "";
    public string GatewayId { get; init; } = "local";
    public string StateDirectory { get; init; } = "";
    public IpcEndpoint Listen { get; init; } = new();
    public PrivateHttp2Settings PrivateHttp2 { get; init; } = new();
    public string IngressAddress { get; init; } = "127.0.0.1";
    public int HttpPort { get; init; } = 80;
    public int HttpsPort { get; init; } = 443;
    public int ManagementPort { get; init; }
    public SecretText IpcToken { get; init; } = SecretText.Empty;
    public SecretText AdministratorToken { get; init; } = SecretText.Empty;
    public int MaxConcurrentExchanges { get; init; } = 256;
    public bool ForwardingDiagnostics { get; init; }
    public bool TransportDiagnostics { get; init; }
    public ControllerBootstrap? Controller { get; init; }

    public void Validate()
    {
        if (SchemaVersion != 1 || SiteId.Length is < 1 or > 128 || NodeId.Length is < 1 or > 128 || GatewayId.Length is < 1 or > 128)
            throw new InvalidDataException("Invalid Application bootstrap identity or version.");
        if (!Path.IsPathFullyQualified(StateDirectory) || !System.Net.IPAddress.TryParse(IngressAddress, out _))
            throw new InvalidDataException("Application requires an absolute state directory and literal ingress address.");
        if (HttpPort is < 0 or > 65535 || HttpsPort is < 0 or > 65535 || (HttpPort == 0 && HttpsPort == 0) || HttpPort == HttpsPort)
            throw new InvalidDataException("Invalid public listener ports.");
        if (MaxConcurrentExchanges is < 1 or > 4096) throw new InvalidDataException("Invalid Application exchange admission limit.");
        PrivateHttp2.Validate();
        Listen.Validate();
        _ = IpcToken.RequireToken();
        if (!AdministratorToken.IsEmpty)
        {
            _ = AdministratorToken.RequireToken();
            if (AdministratorToken.Equals(IpcToken)) throw new InvalidDataException("Administrator and private IPC credentials must be distinct.");
        }
        Controller?.Validate();
        if (ManagementPort is < 0 or > 65535 || (ManagementPort > 0 && (Controller is null || AdministratorToken.IsEmpty || ManagementPort == HttpPort || ManagementPort == HttpsPort || ManagementPort == Controller.RegistrationPort)))
            throw new InvalidDataException("Management requires a distinct enrolled listener and administrator credential.");
        if (Controller is not null && (Controller.RegistrationPort == HttpPort || Controller.RegistrationPort == HttpsPort))
            throw new InvalidDataException("Registration and serving ports must be distinct.");
    }
}
