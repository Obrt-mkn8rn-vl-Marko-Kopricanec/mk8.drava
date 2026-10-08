namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed class AcmeCertificateStatusStore(IAcmeCertificateStatusPersistence? persistence = null)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AcmeCertificateLifecycleStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private Task? _initialization;

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (persistence is null) return ValueTask.CompletedTask;
        lock (_gate) return new ValueTask(_initialization ??= LoadAsync(cancellationToken));
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var statuses = await persistence!.ReadAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
            foreach (var status in statuses) _statuses.TryAdd(status.CertificateId, status);
    }

    public async ValueTask UpsertAsync(AcmeCertificateLifecycleStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);
        cancellationToken.ThrowIfCancellationRequested();
        if (persistence is not null) await persistence.UpsertAsync(status, cancellationToken).ConfigureAwait(false);
        Upsert(status);
    }
    public IReadOnlyList<AcmeCertificateLifecycleStatus> Snapshot()
    {
        lock (_gate)
        {
            return _statuses.Values.OrderBy(static status => status.CertificateId, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public AcmeCertificateLifecycleStatus? Get(string certificateId)
    {
        lock (_gate)
        {
            return _statuses.TryGetValue(certificateId, out var status) ? status : null;
        }
    }

    public void Upsert(AcmeCertificateLifecycleStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            _statuses[status.CertificateId] = status;
        }
    }
}
