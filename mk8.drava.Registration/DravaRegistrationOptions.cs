using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Discovery;

namespace Mk8.Drava.Registration;

public sealed record DravaRegistrationOptions
{
    public RegistrationSiteTrust Site { get; init; } = new();
    public string NodeId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string ServiceId { get; init; } = "";
    public string ContractId { get; init; } = "v1";
    public string DeploymentId { get; init; } = "default";
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public string ReadinessPath { get; init; } = "";
    public string AdvertiseAddress { get; init; } = "";
    public string UpstreamProtocol { get; init; } = "http1";
    public string Zone { get; init; } = "local";
    public int Weight { get; init; } = 1;
    public int PendingRetrySeconds { get; init; } = 5;
    public int RenewJitterPercent { get; init; } = 20;
    public int ShutdownDeadlineMilliseconds { get; init; } = 3000;
    public int AgentDrainDeadlineMilliseconds { get; init; } = 1000;
    public bool MulticastDiscovery { get; init; } = true;
    public IReadOnlyList<DiscoveryCandidate> GatewaySeeds { get; init; } = [];

    internal DravaRegistrationOptions CopyValidated()
    {
        if (Site is null || GatewaySeeds is null) throw new InvalidDataException("Enrollment and seed collection are required.");
        Site.Validate();
        if (PendingRetrySeconds is < 1 or > 60 || RenewJitterPercent is < 0 or > 25 || ShutdownDeadlineMilliseconds is < 100 or > 30_000 ||
            AgentDrainDeadlineMilliseconds is < 100 or > 10_000 || AgentDrainDeadlineMilliseconds >= ShutdownDeadlineMilliseconds)
            throw new InvalidDataException("Invalid SDK retry or shutdown settings.");
        RegistrationSiteTrust.RequireLabel(NodeId);
        RegistrationSiteTrust.RequireLabel(OwnerId);
        RegistrationSiteTrust.RequireLabel(ServiceId);
        RegistrationSiteTrust.RequireLabel(ContractId);
        RegistrationSiteTrust.RequireLabel(DeploymentId);
        RegistrationSiteTrust.RequireLabel(Zone);
        if (!Guid.TryParseExact(InstanceId, "N", out var instance) || instance == Guid.Empty || !string.Equals(instance.ToString("N"), InstanceId, StringComparison.Ordinal)) throw new InvalidDataException("Instance identity requires a nonempty canonical GUID.");
        if (Weight is < 1 or > 100_000 || UpstreamProtocol is not "http1" and not "http2" || GatewaySeeds.Count > 16) throw new InvalidDataException("Invalid registration endpoint settings.");
        if (AdvertiseAddress.Length > 0) _ = new DiscoveryCandidate(AdvertiseAddress, 1);
        if (ReadinessPath.Length is < 1 or > 1024 || !ReadinessPath.StartsWith('/') || ReadinessPath.StartsWith("//", StringComparison.Ordinal)) throw new InvalidDataException("Registration requires a declared readiness origin path.");
        foreach (var character in ReadinessPath)
            if (char.IsControl(character) || character == ' ' || character > 127 || character == (char)92 || character == '#') throw new InvalidDataException("Invalid declared readiness path.");
        var seeds = new DiscoveryCandidate[GatewaySeeds.Count];
        for (var index = 0; index < seeds.Length; index++) seeds[index] = GatewaySeeds[index] ?? throw new InvalidDataException("Gateway seeds cannot contain null entries.");
        return this with { GatewaySeeds = Array.AsReadOnly(seeds) };
    }
}
