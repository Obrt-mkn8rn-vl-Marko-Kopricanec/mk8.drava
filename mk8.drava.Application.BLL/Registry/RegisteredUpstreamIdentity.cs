namespace Mk8.Drava.Application.BLL.Registry;

public sealed record RegisteredUpstreamIdentity
{
    public RegisteredUpstreamIdentity(string nodeId, string ownerId, string serviceId, string contractId, string instanceId, string bootId)
    {
        RegistryNames.RequireLabel(nodeId);
        RegistryNames.RequireLabel(ownerId);
        RegistryNames.RequireLabel(serviceId);
        RegistryNames.RequireLabel(contractId);
        RegistryNames.RequireEpoch(instanceId);
        RegistryNames.RequireEpoch(bootId);
        NodeId = nodeId;
        OwnerId = ownerId;
        ServiceId = serviceId;
        ContractId = contractId;
        InstanceId = instanceId;
        BootId = bootId;
    }

    public string NodeId { get; }
    public string OwnerId { get; }
    public string ServiceId { get; }
    public string ContractId { get; }
    public string InstanceId { get; }
    public string BootId { get; }
    public string Partition => $"{NodeId}|{OwnerId}|{ServiceId}|{ContractId}|{InstanceId}|{BootId}";
}
