using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IAcmeCertificateMaterialWriter
{
    void EnsureLayout(string dataDirectory, string storagePath);
    RuntimeCertificate WriteAndLoad(AcmeCertificateMaterialWriteRequest request);
    ValueTask<RuntimeCertificate> WriteAndLoadAsync(AcmeCertificateMaterialWriteRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(WriteAndLoad(request));
    }
}
