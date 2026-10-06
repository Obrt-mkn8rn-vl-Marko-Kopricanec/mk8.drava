namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHttpsRedirectProjection
{
    public RuntimeHttpsRedirectProjection(bool Enabled, int StatusCode, int? HttpsPort)
    {
        RuntimeRedirectFacts.ValidateHttpsRedirect(StatusCode, HttpsPort);
        this.Enabled = Enabled;
        this.StatusCode = StatusCode;
        this.HttpsPort = HttpsPort;
    }

    public bool Enabled { get; }
    public int StatusCode { get; }
    public int? HttpsPort { get; }
}
