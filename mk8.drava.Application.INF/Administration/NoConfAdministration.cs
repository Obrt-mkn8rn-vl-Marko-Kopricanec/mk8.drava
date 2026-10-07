using System.Text.Json;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Administration;

public sealed partial class NoConfAdministration(NoConfReconciler reconciler, RegistryCoordinator registry, DestinationAvailabilityStore availability, string siteId)
{
    public bool StorageHealthy => registry.StorageHealthy;

    public async ValueTask<ControlReply> ExecuteAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Operation switch
        {
            ControlOperation.PolicyQuery => await QueryPolicyAsync(request, cancellationToken).ConfigureAwait(false),
            ControlOperation.PolicyUpdate => await UpdatePolicyAsync(request, cancellationToken).ConfigureAwait(false),
            ControlOperation.PolicyRollback => await RollbackPolicyAsync(request, cancellationToken).ConfigureAwait(false),
            ControlOperation.RegistryQuery => QueryRegistry(request),
            ControlOperation.NodeRevoke => await RevokeNodeAsync(request, cancellationToken).ConfigureAwait(false),
            ControlOperation.InstanceRevoke => await RevokeInstanceAsync(request, cancellationToken).ConfigureAwait(false),
            _ => new ControlReply { StatusCode = 501, SafeReason = "Control operation is not supported." },
        };
    }

    private async ValueTask<ControlReply> QueryPolicyAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        var query = request.JsonPayload.IsEmpty ? new PolicyQueryRequest() : ControlJson.Read<PolicyQueryRequest>(request.JsonPayload);
        ValidatePage(query.AfterServiceId, query.Limit, epoch: false);
        var view = await reconciler.ReadPolicyViewAsync(query.IncludeHistory, cancellationToken).ConfigureAwait(false);
        return ControlJson.Reply(MapPolicy(view, query));
    }

    private async ValueTask<ControlReply> UpdatePolicyAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        var update = ControlJson.Read<PolicyUpdateRequest>(request.JsonPayload);
        if (update.ImportFile ? update.Policy.ValueKind != JsonValueKind.Undefined : update.Policy.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Select a policy object or an explicit file import.");
        var accepted = await reconciler.UpdatePolicyAsync(update.ExpectedRevision, update.ImportFile ? null : update.Policy.GetRawText(), update.ImportFile, cancellationToken).ConfigureAwait(false);
        return await MutationReplyAsync(accepted, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ControlReply> RollbackPolicyAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        var rollback = ControlJson.Read<PolicyRollbackRequest>(request.JsonPayload);
        var accepted = await reconciler.RollbackPolicyAsync(rollback.ExpectedRevision, rollback.TargetRevision, cancellationToken).ConfigureAwait(false);
        return await MutationReplyAsync(accepted, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ControlReply> MutationReplyAsync(bool accepted, CancellationToken cancellationToken)
    {
        var view = await reconciler.ReadPolicyViewAsync(includeHistory: false, cancellationToken).ConfigureAwait(false);
        var reply = ControlJson.Reply(MapPolicy(view, new PolicyQueryRequest()));
        reply.StatusCode = !accepted ? 409u : view.Accepted?.Revision != view.AppliedRevision ? 202u : 200u;
        return reply;
    }

    private async ValueTask<ControlReply> RevokeNodeAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        var revoke = ControlJson.Read<NodeRevokeRequest>(request.JsonPayload);
        await registry.RevokeNodeAsync(revoke.NodeId, "administrator", cancellationToken).ConfigureAwait(false);
        return ControlJson.Reply(new { revision = registry.State.Revision });
    }

    private async ValueTask<ControlReply> RevokeInstanceAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        var identity = ControlJson.Read<RegistrationIdentity>(request.JsonPayload);
        if (!string.Equals(identity.SiteId, siteId, StringComparison.Ordinal)) throw new InvalidDataException("Instance belongs to another site.");
        var domain = new RegisteredUpstreamIdentity(identity.NodeId, identity.OwnerId, identity.ServiceId, identity.ContractId, identity.InstanceId, identity.BootId);
        await registry.RevokeInstanceAsync(domain, "administrator", cancellationToken).ConfigureAwait(false);
        return ControlJson.Reply(new { revision = registry.State.Revision });
    }

    private static PolicyStateResponse MapPolicy(NoConfPolicyView view, PolicyQueryRequest query)
    {
        var services = view.Compiled?.Services ?? new Dictionary<string, CompiledNoConfService>(StringComparer.Ordinal);
        var page = PageKeys(services, query.AfterServiceId, query.Limit);
        var explanations = new List<ServicePolicyResponse>();
        foreach (var key in page.Keys)
        {
            var service = services[key];
            explanations.Add(new ServicePolicyResponse { ServiceId = key, Host = service.Route.Host,
                PathPrefix = service.Route.PathPrefix, Action = service.Route.Action.ToString(),
                Algorithm = service.Route.Balancing?.Algorithm.ToString() ?? "", Provenance = service.Provenance });
        }
        var history = new List<PolicyRevisionResponse>();
        foreach (var revision in view.History)
            history.Add(new PolicyRevisionResponse { Revision = revision.Revision, Digest = revision.Digest,
                AcceptedAtUtc = revision.AcceptedAtUtc, Actor = revision.Actor, Source = revision.Source });
        return new PolicyStateResponse
        {
            AcceptedRevision = view.Accepted?.Revision ?? 0, AppliedRevision = view.AppliedRevision, RuntimeVersion = view.RuntimeVersion,
            RegistryRevision = view.RegistryRevision, Digest = view.Accepted?.Digest ?? "", Source = view.Accepted?.Source ?? "",
            CanonicalJson = view.Accepted?.CanonicalJson ?? "", Failure = view.Failure, History = history.AsReadOnly(),
            Services = explanations.AsReadOnly(), NextServiceId = page.Next,
        };
    }

    private static void ValidatePage(string afterId, int limit, bool epoch)
    {
        if (limit is < 1 or > 250) throw new InvalidDataException("Query page size is out of bounds.");
        if (afterId.Length > 0)
        {
            if (epoch) RegistryNames.RequireEpoch(afterId);
            else RegistryNames.RequireLabel(afterId);
        }
    }

    private static (IReadOnlyList<string> Keys, string Next) PageKeys<T>(IReadOnlyDictionary<string, T> values, string afterId, int limit)
    {
        var keys = new List<string>();
        foreach (var key in values.Keys)
            if (string.Compare(key, afterId, StringComparison.Ordinal) > 0) keys.Add(key);
        keys.Sort(StringComparer.Ordinal);
        var selected = keys.GetRange(0, Math.Min(keys.Count, limit));
        return (selected.AsReadOnly(), keys.Count > limit ? selected[^1] : "");
    }
}
