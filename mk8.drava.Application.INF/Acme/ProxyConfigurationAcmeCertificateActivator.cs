using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;

namespace Mk8.Drava.Application.INF.Acme;
public sealed class ProxyConfigurationAcmeCertificateActivator : IAcmeCertificateActivator
{
    private readonly IProxyActiveConfigurationSnapshotReader _snapshotReader;
    private readonly IProxyActiveConfigurationSnapshotWriter _snapshotWriter;
    public ProxyConfigurationAcmeCertificateActivator(IProxyActiveConfigurationSnapshotReader snapshotReader, IProxyActiveConfigurationSnapshotWriter snapshotWriter)
    {
        _snapshotReader = snapshotReader;
        _snapshotWriter = snapshotWriter;
    }

    public void Activate(RuntimeCertificate certificate)
    {
        var snapshot = _snapshotReader.Snapshot;
        Dictionary<string, RuntimeCertificate> certificates = new(snapshot.Certificates, StringComparer.OrdinalIgnoreCase)
        {
            [certificate.Id] = certificate
        };
        _snapshotWriter.Replace(snapshot.WithCertificates(certificates));
    }
}
