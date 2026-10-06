namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed class AcmeCertificateStatusStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AcmeCertificateLifecycleStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
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
