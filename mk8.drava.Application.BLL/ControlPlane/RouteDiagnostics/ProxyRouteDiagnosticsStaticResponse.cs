namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDiagnosticsStaticResponse
{
    public ProxyRouteDiagnosticsStaticResponse(int StatusCode, string ContentType, string Body)
    {
        ArgumentNullException.ThrowIfNull(ContentType);
        ArgumentNullException.ThrowIfNull(Body);
        this.StatusCode = StatusCode;
        this.ContentType = ContentType;
        this.Body = Body;
    }

    public int StatusCode { get; }
    public string ContentType { get; }
    public string Body { get; }
}
