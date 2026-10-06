namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed class ProxyAcmeStatusSnapshotReader : IProxyAcmeStatusSnapshotReader
{
    private readonly IProxyAcmeStatusConfigurationSource _configurationSource;
    private readonly IProxyAcmeCertificateLifecycleStatusSource _lifecycleStatusSource;
    public ProxyAcmeStatusSnapshotReader(IProxyAcmeStatusConfigurationSource configurationSource, IProxyAcmeCertificateLifecycleStatusSource lifecycleStatusSource)
    {
        _configurationSource = configurationSource;
        _lifecycleStatusSource = lifecycleStatusSource;
    }

    public ProxyAcmeStatusSnapshotReadResult ReadSnapshot()
    {
        var sourceResult = _configurationSource.Read();
        if (sourceResult is not ProxyAcmeStatusConfigurationSourceReadResult.AvailableResult available)
        {
            return ProxyAcmeStatusSnapshotReadResult.MissingConfiguration;
        }

        var sourceSnapshot = available.Snapshot;
        return ProxyAcmeStatusSnapshotReadResult.Available(new ProxyAcmeStatusSnapshot(sourceSnapshot.Enabled, sourceSnapshot.DirectoryUrl, sourceSnapshot.UseStaging, sourceSnapshot.Certificates, ProxyAcmeRuntimeCertificateStatusMapper.FromSources(sourceSnapshot.RuntimeCertificates)));
    }

    public IReadOnlyList<AcmeCertificateLifecycleStatus> GetLifecycleStatuses()
    {
        return _lifecycleStatusSource.GetLifecycleStatuses();
    }
}
