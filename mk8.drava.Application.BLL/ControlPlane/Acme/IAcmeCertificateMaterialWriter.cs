using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IAcmeCertificateMaterialWriter
{
    void EnsureLayout(string dataDirectory, string storagePath);
    RuntimeCertificate WriteAndLoad(AcmeCertificateMaterialWriteRequest request);
}
