namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed class ProxyAcmeAdministrationService
{
    private readonly IProxyAcmeStatusSnapshotReader _statusReader;
    public ProxyAcmeAdministrationService(IProxyAcmeStatusSnapshotReader statusReader)
    {
        _statusReader = statusReader;
    }

    public AcmeStatus? GetStatus()
    {
        var snapshotResult = _statusReader.ReadSnapshot();
        if (snapshotResult is not ProxyAcmeStatusSnapshotReadResult.AvailableResult available)
        {
            return null;
        }

        var snapshot = available.Snapshot;
        var statusById = _statusReader.GetLifecycleStatuses().ToDictionary(static status => status.CertificateId, StringComparer.OrdinalIgnoreCase);
        var statuses = snapshot.Certificates.Select(certificate =>
        {
            if (statusById.TryGetValue(certificate.Id, out var status))
            {
                return status;
            }

            snapshot.RuntimeCertificates.TryGetValue(certificate.Id, out var runtimeCertificate);
            return AcmeCertificateLifecycleStatus.FromConfiguredCertificate(certificate, ToActiveCertificate(runtimeCertificate));
        });
        return AcmeStatus.FromSources(snapshot.Enabled, snapshot.DirectoryUrl, snapshot.UseStaging, statuses);
    }

    private static AcmeCertificateLifecycleActiveCertificate? ToActiveCertificate(ProxyAcmeRuntimeCertificateStatus? runtimeCertificate)
    {
        return runtimeCertificate is not null && string.Equals(runtimeCertificate.Source, "acme", StringComparison.OrdinalIgnoreCase) ? new AcmeCertificateLifecycleActiveCertificate(runtimeCertificate.NotBeforeUtc, runtimeCertificate.NotAfterUtc) : null;
    }
}
