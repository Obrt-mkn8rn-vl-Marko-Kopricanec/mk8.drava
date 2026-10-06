namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public interface IProxyRouteDiagnosticsPathRewritePolicy
{
    string Apply(IProxyRouteDiagnosticsRoute route, string target, string path);
}
