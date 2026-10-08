using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using System.Globalization;

namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed class ProxyRouteActionPolicy
{
    public RouteActionDecision Evaluate(ProxyRouteActionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var policyRedirect = input.IsUpgradeRequest ? ProxyRoutePolicyRedirectDecision.NoRedirect : ProxyRoutePolicyRedirectEvaluator.Evaluate(input.PolicyRedirect);
        if (policyRedirect is ProxyRoutePolicyRedirectDecision.RedirectDecision redirect)
        {
            return new RouteActionDecision(RedirectResponse(redirect.StatusCode, redirect.Location));
        }

        if (input.Maintenance.Enabled)
        {
            List<ProxyHeaderField> headers = [];
            if (input.Maintenance.RetryAfterSeconds.HasValue)
            {
                headers.Add(new ProxyHeaderField("Retry-After", input.Maintenance.RetryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture)));
            }

            return new RouteActionDecision(new GeneratedRouteResponse(503, "Service Unavailable", input.Maintenance.ContentType, input.Maintenance.Body, headers));
        }

        return input.Action switch
        {
            ProxyRouteActionKind.Redirect => new RouteActionDecision(BuildRouteRedirect(input.Redirect, input.PolicyRedirect.RequestTarget)),
            ProxyRouteActionKind.StaticResponse => new RouteActionDecision(new GeneratedRouteResponse(input.StaticResponse.StatusCode, ReasonPhrase(input.StaticResponse.StatusCode), input.StaticResponse.ContentType, input.StaticResponse.Body, [])),
            _ => RouteActionDecision.Proxy
        };
    }

    private static GeneratedRouteResponse BuildRouteRedirect(ProxyRouteRedirectActionInput redirect, string requestTarget)
    {
        var location = !string.IsNullOrWhiteSpace(redirect.TargetUrl) ? redirect.TargetUrl : redirect.TargetPath;
        if (redirect.PreserveQuery)
        {
            var query = ExtractQuery(requestTarget);
            if (!string.IsNullOrEmpty(query) && !location.Contains('?', StringComparison.Ordinal))
            {
                location += query;
            }
        }

        return RedirectResponse(redirect.StatusCode, location);
    }

    private static GeneratedRouteResponse RedirectResponse(int statusCode, string location)
    {
        return new GeneratedRouteResponse(statusCode, ReasonPhrase(statusCode), null, "", [new ProxyHeaderField("Location", location)]);
    }

    private static string ExtractQuery(string target)
    {
        var queryIndex = target.IndexOf('?', StringComparison.Ordinal);
        return queryIndex < 0 ? "" : target[queryIndex..];
    }

    public static string ReasonPhrase(int statusCode)
    {
        return statusCode switch
        {
            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            204 => "No Content",
            301 => "Moved Permanently",
            302 => "Found",
            303 => "See Other",
            307 => "Temporary Redirect",
            308 => "Permanent Redirect",
            400 => "Bad Request",
            404 => "Not Found",
            408 => "Request Timeout",
            410 => "Gone",
            413 => "Payload Too Large",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            _ => "Status"
        };
    }
}
