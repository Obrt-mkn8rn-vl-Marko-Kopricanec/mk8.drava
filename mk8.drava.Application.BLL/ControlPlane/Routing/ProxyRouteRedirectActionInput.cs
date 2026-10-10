using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using System.Globalization;

namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
[method: System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "This member carries exact redirect Location text; empty URL selects the separate target path, and configuration validation owns absolute-URL checks. Preserve escaping, query text and the existing string API.")]
public sealed record ProxyRouteRedirectActionInput(
    int StatusCode,
    #pragma warning disable CA1056 // This generated property's diagnostic is located on this positional parameter; preserve optional exact Location text and external syntax validation.
    string TargetUrl,
    #pragma warning restore CA1056
    string TargetPath,
    bool PreserveQuery);
