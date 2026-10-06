namespace Mk8.Drava.Contracts.Registration.V1;

public sealed record ServiceAdvertisement
{
    public string DeploymentId { get; init; } = "";
    public string Address { get; init; } = "";
    public int Port { get; init; }
    public string Scheme { get; init; } = "http";
    public string Protocol { get; init; } = "http1";
    public string ReadinessPath { get; init; } = "";
    public string Zone { get; init; } = "local";
    public int Weight { get; init; } = 1;
}
