using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Administration;

public sealed partial class NoConfAdministration
{
    private ControlReply QueryRegistry(ControlRequest request)
    {
        var query = request.JsonPayload.IsEmpty ? new RegistryQueryRequest() : ControlJson.Read<RegistryQueryRequest>(request.JsonPayload);
        if (query.Kind is not ("instances" or "nodes")) throw new InvalidDataException("Unknown registry query kind.");
        ValidatePage(query.AfterId, query.Limit, epoch: string.Equals(query.Kind, "instances", StringComparison.Ordinal));
        var state = registry.State;
        var nodes = new List<NodeGrantResponse>();
        var instances = new List<RegisteredInstanceResponse>();
        string next;
        if (string.Equals(query.Kind, "nodes", StringComparison.Ordinal))
        {
            var page = PageKeys(state.Grants, query.AfterId, query.Limit);
            next = page.Next;
            foreach (var key in page.Keys)
            {
                var grant = state.Grants[key];
                nodes.Add(new NodeGrantResponse { NodeId = grant.NodeId, OwnerId = grant.OwnerId,
                    CertificateFingerprint = grant.CertificateFingerprint, ServicePrefix = grant.ServicePrefix,
                    EndpointAddresses = grant.EndpointAddresses, MinimumPort = grant.MinimumPort, MaximumPort = grant.MaximumPort,
                    NotAfterUtc = grant.NotAfterUtc, Revoked = grant.Revoked });
            }
        }
        else
        {
            var page = PageKeys(state.Instances, query.AfterId, query.Limit);
            next = page.Next;
            foreach (var key in page.Keys)
            {
                var intent = state.Instances[key];
                var status = availability.Status(intent.Identity);
                instances.Add(new RegisteredInstanceResponse
                {
                    Identity = new RegistrationIdentity { SiteId = siteId, NodeId = intent.Identity.NodeId, OwnerId = intent.Identity.OwnerId,
                        ServiceId = intent.Identity.ServiceId, ContractId = intent.Identity.ContractId, InstanceId = intent.Identity.InstanceId, BootId = intent.Identity.BootId },
                    Address = intent.Address, Port = intent.Port, Protocol = intent.Protocol, Scheme = intent.Scheme, Draining = intent.Draining,
                    LeaseValid = status.LeaseValid, ReadinessValid = status.ReadinessValid, PublicationValid = status.PublicationValid,
                    Revoked = status.Revoked || state.IsTombstoned(intent.Identity),
                });
            }
        }
        return ControlJson.Reply(new RegistryStateResponse { Revision = state.Revision, StorageHealthy = registry.StorageHealthy,
            TombstoneCount = state.Tombstones.Count, NextId = next, Nodes = nodes.AsReadOnly(), Instances = instances.AsReadOnly() });
    }
}
