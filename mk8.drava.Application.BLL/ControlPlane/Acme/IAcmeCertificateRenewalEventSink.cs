namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IAcmeCertificateRenewalEventSink
{
    void RenewalFailed(string certificateId, string? errorSummary);
}
