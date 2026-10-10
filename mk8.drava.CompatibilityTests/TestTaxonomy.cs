namespace Mk8.Drava.CompatibilityTests;

internal static class TestTaxonomy
{
    public const string Http1 = "HTTP1";
    public const string Http2 = "HTTP2";
    public const string Http3 = "HTTP3";
    public const string UpstreamHttp1 = "UpstreamHTTP1";
    public const string UpstreamHttp2 = "UpstreamHTTP2";
    public const string UpstreamHttp3 = "UpstreamHTTP3";
    public const string Config = "Config";
    public const string Routing = "Routing";
    public const string Tls = "TLS";
    public const string Headers = "Headers";
    public const string Caching = "Caching";
    public const string RetryCircuit = "RetryCircuit";
    public const string HealthChecks = "HealthChecks";
    public const string Limits = "Limits";
    public const string Admin = "Admin";
    public const string Metrics = "Metrics";
    public const string SecurityNegativePaths = "SecurityNegativePaths";
    public static readonly string[] Categories = [Http1, Http2, Http3, UpstreamHttp1, UpstreamHttp2, UpstreamHttp3, Config, Routing, Tls, Headers, Caching, RetryCircuit, HealthChecks, Limits, Admin, Metrics, SecurityNegativePaths];
    private static readonly Dictionary<string, string> CategoryLookup = Categories.ToDictionary(static category => category, static category => category, StringComparer.OrdinalIgnoreCase);
    public static bool IsKnownCategory(string category)
    {
        return CategoryLookup.ContainsKey(category);
    }

    public static string CanonicalCategory(string category)
    {
        return CategoryLookup.TryGetValue(category, out var canonical) ? canonical : throw new ArgumentException($"Unknown test category: {category}", nameof(category));
    }

    public static IReadOnlySet<string> CanonicalCategories(params string[] categories)
    {
        HashSet<string> canonical = new(StringComparer.Ordinal);
        foreach (var category in categories)
        {
            canonical.Add(CanonicalCategory(category));
        }

        if (canonical.Count == 0)
        {
            throw new ArgumentException("Each test registration must declare at least one correctness category.", nameof(categories));
        }

        return canonical;
    }
}
