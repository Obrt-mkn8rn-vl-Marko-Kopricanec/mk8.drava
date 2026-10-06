namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDiagnosticsHttpsRedirectPolicy(bool Enabled, int StatusCode, int? HttpsPort);
