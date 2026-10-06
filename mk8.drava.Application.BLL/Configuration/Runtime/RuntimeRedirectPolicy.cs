namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeRedirectPolicy
{
    public RuntimeRedirectPolicy(int StatusCode, string TargetUrl, string TargetPath, bool PreserveQuery)
    {
        RuntimeRedirectFacts.ValidateRouteRedirect(StatusCode, TargetUrl, TargetPath);
        this.StatusCode = StatusCode;
        this.TargetUrl = TargetUrl;
        this.TargetPath = TargetPath;
        this.PreserveQuery = PreserveQuery;
    }

    public int StatusCode { get; }
    public string TargetUrl { get; }
    public string TargetPath { get; }
    public bool PreserveQuery { get; }
}
