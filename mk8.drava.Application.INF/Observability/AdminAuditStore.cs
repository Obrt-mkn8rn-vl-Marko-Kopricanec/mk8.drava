using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.Observability;

namespace Mk8.Drava.Application.INF.Observability;
public sealed class AdminAuditStore : IProxyAdminAuditReader, IProxyAdminAuditRecorder
{
    public const int MaximumReadLimit = 500;
    private readonly IProxyLogPersistenceStore _logPersistenceStore;
    private readonly Lock _gate = new();
    private readonly LinkedList<ProxyAdminAuditEvent> _events = new();
    public AdminAuditStore(IProxyLogPersistenceStore logPersistenceStore)
    {
        _logPersistenceStore = logPersistenceStore;
    }

    public void Add(ProxyAdminAuditEvent auditEvent, int capacity)
    {
        lock (_gate)
        {
            var boundedCapacity = Math.Max(1, capacity);
            _events.AddFirst(auditEvent);
            while (_events.Count > boundedCapacity)
            {
                _events.RemoveLast();
            }
        }

        _logPersistenceStore.WriteAdminAudit(auditEvent);
    }

    public IReadOnlyList<ProxyAdminAuditEvent> Recent(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumReadLimit);
        List<ProxyAdminAuditEvent> results = [];
        lock (_gate)
        {
            var current = _events.First;
            while (current is not null && results.Count < boundedLimit)
            {
                results.Add(current.Value);
                current = current.Next;
            }
        }

        return results.AsReadOnly();
    }
}
