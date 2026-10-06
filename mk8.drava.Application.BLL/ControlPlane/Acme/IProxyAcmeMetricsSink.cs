namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IProxyAcmeMetricsSink
{
    void AcmeRenewalAttempted();
    void AcmeRenewalSucceeded();
    void AcmeRenewalFailed();
}
