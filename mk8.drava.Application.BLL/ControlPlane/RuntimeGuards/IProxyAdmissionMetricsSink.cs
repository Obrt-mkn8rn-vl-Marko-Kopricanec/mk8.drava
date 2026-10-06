namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
public interface IProxyAdmissionMetricsSink
{
    void ConnectionAdmissionRejected();
    void TlsHandshakeStarted();
    void TlsHandshakeEnded();
    void TlsHandshakeAdmissionRejected();
}
