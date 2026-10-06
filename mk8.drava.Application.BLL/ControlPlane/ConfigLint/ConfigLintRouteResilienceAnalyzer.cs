using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintRouteResilienceAnalyzer
{
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintRoute route, string routePath, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(route);
        List<ConfigLintFinding> findings = [];
        if (route.RetryEnabled && route.RetryMethods.Any(static method => !ProxyRequestMethodPolicy.IsSafeReadMethod(method)))
        {
            findings.Add(ConfigLintFindingFactory.Error("retry_unsafe_method", $"Route '{route.Name}' allows retry for an unsafe method.", sourceName, routePath, "Restrict retry methods to GET and HEAD."));
        }

        if (route.Upstreams.Any(static upstream => upstream.CircuitBreakerEnabled) && (route.Upstreams.Count < 2 || !route.HealthCheckEnabled))
        {
            findings.Add(ConfigLintFindingFactory.Warning("circuit_breaker_low_redundancy", $"Route '{route.Name}' configures a circuit breaker without multiple upstreams or active health checks.", sourceName, routePath, "Circuit breakers are most useful with redundant upstreams and health checks."));
        }

        return findings;
    }
}
