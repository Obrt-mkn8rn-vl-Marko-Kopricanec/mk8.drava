namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed class ProxyStatusOperations : IProxyStatusOperations
{
    private readonly IProxyStatusInputReader _inputReader;
    public ProxyStatusOperations(IProxyStatusInputReader inputReader)
    {
        _inputReader = inputReader;
    }

    public ProxyStatus GetStatus()
    {
        return ProxyStatusBuilder.Build(_inputReader.Read());
    }
}
