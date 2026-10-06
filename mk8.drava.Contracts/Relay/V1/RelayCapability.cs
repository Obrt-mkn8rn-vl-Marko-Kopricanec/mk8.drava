using Mk8.Drava.Contracts.Registration.V1;

namespace Mk8.Drava.Contracts.Relay.V1;

public sealed record RelayCapability
{
    public uint Version { get; init; } = 1;
    public RegistrationIdentity Identity { get; init; } = new();
    public ServiceAdvertisement Advertisement { get; init; } = new();
    public string ExchangeId { get; init; } = "";
    public string CapabilityId { get; init; } = "";
    public string ControllerEpoch { get; init; } = "";
    public string AgentBootId { get; init; } = "";
    public RelayPurpose Purpose { get; init; }
    public string TlsServerName { get; init; } = "";
    public bool ValidateBackendCertificate { get; init; } = true;
    public long IssuedAtUnixMilliseconds { get; init; }
    public long ExpiresAtUnixMilliseconds { get; init; }
    public long MaximumBytesPerDirection { get; init; }
    public int MaximumDurationSeconds { get; init; }
}
