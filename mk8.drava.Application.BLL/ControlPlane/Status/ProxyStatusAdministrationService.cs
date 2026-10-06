namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed class ProxyStatusAdministrationService
{
    private readonly IProxyStatusOperations _operations;
    public ProxyStatusAdministrationService(IProxyStatusOperations operations)
    {
        _operations = operations;
    }

    public ProxyStatus GetStatus()
    {
        return _operations.GetStatus();
    }
}
