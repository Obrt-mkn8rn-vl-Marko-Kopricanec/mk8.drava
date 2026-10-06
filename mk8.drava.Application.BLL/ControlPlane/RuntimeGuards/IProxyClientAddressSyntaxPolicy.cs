namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
public interface IProxyClientAddressSyntaxPolicy
{
    bool IsIpLiteral(string value);
}
