namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyRedirectOptions
{
    public int? StatusCode { get; init; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "This member carries exact redirect Location text; empty URL selects the separate target path, and configuration validation owns absolute-URL checks. Preserve escaping, query text and the existing string API.")]
    public string TargetUrl { get; init; } = "";
    public string TargetPath { get; init; } = "";
    public bool PreserveQuery { get; init; } = true;
}
