namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed class ProxyCacheAdministrationService
{
    private const string ManualClearReason = "manual";
    private readonly IProxyCacheStatusReader _statusReader;
    private readonly IProxyCacheControl _cacheControl;
    public ProxyCacheAdministrationService(IProxyCacheStatusReader statusReader, IProxyCacheControl cacheControl)
    {
        _statusReader = statusReader;
        _cacheControl = cacheControl;
    }

    public ProxyCacheStatus GetStatus()
    {
        return _statusReader.GetStatus();
    }

    public ProxyCacheStatus Clear()
    {
        _cacheControl.Clear(ManualClearReason);
        return _statusReader.GetStatus();
    }
}
