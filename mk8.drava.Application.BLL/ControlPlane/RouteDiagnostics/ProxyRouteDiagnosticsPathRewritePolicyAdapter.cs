using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed class ProxyRouteDiagnosticsPathRewritePolicyAdapter : IProxyRouteDiagnosticsPathRewritePolicy
{
    private readonly PathRewritePolicy _policy = new();
    public string Apply(IProxyRouteDiagnosticsRoute route, string target, string path)
    {
        ArgumentNullException.ThrowIfNull(route);
        var rewrite = route.PathRewrite;
        return _policy.Apply(new PathRewritePolicyInput(rewrite.StripPrefix, rewrite.ReplacePrefix, rewrite.Replacement), target, path);
    }
}
