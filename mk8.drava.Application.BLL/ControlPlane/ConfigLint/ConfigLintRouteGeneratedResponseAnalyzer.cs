using System.Text;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintRouteGeneratedResponseAnalyzer
{
    private const int MaxGeneratedBodyBytes = 64 * 1024;
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintRoute route, string routePath, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!string.Equals(route.Action, "StaticResponse", StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(route.StaticResponseBody) < MaxGeneratedBodyBytes * 4 / 5)
        {
            return[];
        }

        return[ConfigLintFindingFactory.Warning("static_response_body_near_limit", $"Static response route '{route.Name}' has a body near the generated-response size limit.", sourceName, routePath, "Move larger content behind an upstream application or keep the static body small.")];
    }
}
