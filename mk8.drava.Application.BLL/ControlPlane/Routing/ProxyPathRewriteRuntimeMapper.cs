using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public static class ProxyPathRewriteRuntimeMapper
{
    public static PathRewritePolicyInput ToPolicyInput(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new PathRewritePolicyInput(route.PathRewrite.StripPrefix, route.PathRewrite.ReplacePrefix, route.PathRewrite.Replacement);
    }
}
