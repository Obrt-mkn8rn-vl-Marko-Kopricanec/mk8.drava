namespace Mk8.Drava.Contracts.Registration.V1;

public sealed record RegistrationIdentity
{
    public string SiteId { get; init; } = "";
    public string NodeId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string ServiceId { get; init; } = "";
    public string ContractId { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public string BootId { get; init; } = "";
}
