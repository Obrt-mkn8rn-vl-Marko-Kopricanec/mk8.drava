using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;

namespace Mk8.Drava.Application.DAL.Acme;
public sealed class AcmeCertificateMaterialWriter : IAcmeCertificateMaterialWriter
{
    public void EnsureLayout(string dataDirectory, string storagePath)
    {
        AcmeCertificateMaterialStore.EnsureLayout(dataDirectory, storagePath);
    }

    public RuntimeCertificate WriteAndLoad(AcmeCertificateMaterialWriteRequest request)
    {
        return AcmeCertificateMaterialStore.WriteAndLoad(request);
    }
}
