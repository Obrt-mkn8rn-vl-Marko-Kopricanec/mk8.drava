namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeRedirectPolicy
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "This member carries exact redirect Location text; empty URL selects the separate target path, and configuration validation owns absolute-URL checks. Preserve escaping, query text and the existing string API.")]
    public RuntimeRedirectPolicy(int StatusCode, string TargetUrl, string TargetPath, bool PreserveQuery)
    {
        RuntimeRedirectFacts.ValidateRouteRedirect(StatusCode, TargetUrl, TargetPath);
        this.StatusCode = StatusCode;
        this.TargetUrl = TargetUrl;
        this.TargetPath = TargetPath;
        this.PreserveQuery = PreserveQuery;
    }

    public int StatusCode { get; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "This member carries exact redirect Location text; empty URL selects the separate target path, and configuration validation owns absolute-URL checks. Preserve escaping, query text and the existing string API.")]
    public string TargetUrl { get; }
    public string TargetPath { get; }
    public bool PreserveQuery { get; }
}
