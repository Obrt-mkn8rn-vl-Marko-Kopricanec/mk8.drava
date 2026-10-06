namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
public interface IProxyAdminAuthenticationMetricsSink
{
    void AdminAuthSucceeded();
    void AdminAuthFailed();
}
