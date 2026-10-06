namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDiagnosticsActionDecision
{
    public ProxyRouteDiagnosticsActionDecision(bool ShouldProxy, int? GeneratedStatusCode)
    {
        if (ShouldProxy && GeneratedStatusCode.HasValue)
        {
            throw new ArgumentException("Route diagnostics proxy decisions cannot include a generated status code.", nameof(GeneratedStatusCode));
        }

        if (!ShouldProxy && GeneratedStatusCode is null)
        {
            throw new ArgumentException("Route diagnostics generated-response decisions require a generated status code.", nameof(GeneratedStatusCode));
        }

        this.ShouldProxy = ShouldProxy;
        this.GeneratedStatusCode = GeneratedStatusCode;
    }

    public bool ShouldProxy { get; }
    public int? GeneratedStatusCode { get; }
    public static ProxyRouteDiagnosticsActionDecision Proxy { get; } = new(ShouldProxy: true, GeneratedStatusCode: null);

    public static ProxyRouteDiagnosticsActionDecision GeneratedResponse(int statusCode)
    {
        return new ProxyRouteDiagnosticsActionDecision(ShouldProxy: false, statusCode);
    }
}
