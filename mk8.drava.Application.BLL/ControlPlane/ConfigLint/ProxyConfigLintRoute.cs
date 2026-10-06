namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ProxyConfigLintRoute
{
    public ProxyConfigLintRoute(string Name, string SiteName, string Host, string PathPrefix, string Action, bool HttpsRedirectEnabled, bool CanonicalHostEnabled, string CanonicalHostTargetHost, bool CacheEnabled, IEnumerable<string> CacheVaryByHeaders, bool RetryEnabled, IEnumerable<string> RetryMethods, bool HealthCheckEnabled, IEnumerable<ProxyConfigLintUpstream> Upstreams, string StaticResponseBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(SiteName);
        ArgumentNullException.ThrowIfNull(Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(PathPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(Action);
        ArgumentNullException.ThrowIfNull(CanonicalHostTargetHost);
        ArgumentNullException.ThrowIfNull(CacheVaryByHeaders);
        ArgumentNullException.ThrowIfNull(RetryMethods);
        ArgumentNullException.ThrowIfNull(Upstreams);
        ArgumentNullException.ThrowIfNull(StaticResponseBody);
        this.Name = Name;
        this.SiteName = SiteName;
        this.Host = Host;
        this.PathPrefix = PathPrefix;
        this.Action = Action;
        this.HttpsRedirectEnabled = HttpsRedirectEnabled;
        this.CanonicalHostEnabled = CanonicalHostEnabled;
        this.CanonicalHostTargetHost = CanonicalHostTargetHost;
        this.CacheEnabled = CacheEnabled;
        this.CacheVaryByHeaders = ConfigLintList.Copy(CacheVaryByHeaders);
        this.RetryEnabled = RetryEnabled;
        this.RetryMethods = ConfigLintList.Copy(RetryMethods);
        this.HealthCheckEnabled = HealthCheckEnabled;
        this.Upstreams = ConfigLintList.Copy(Upstreams);
        this.StaticResponseBody = StaticResponseBody;
    }

    public string Name { get; }
    public string SiteName { get; }
    public string Host { get; }
    public string PathPrefix { get; }
    public string Action { get; }
    public bool HttpsRedirectEnabled { get; }
    public bool CanonicalHostEnabled { get; }
    public string CanonicalHostTargetHost { get; }
    public bool CacheEnabled { get; }
    public IReadOnlyList<string> CacheVaryByHeaders { get; }
    public bool RetryEnabled { get; }
    public IReadOnlyList<string> RetryMethods { get; }
    public bool HealthCheckEnabled { get; }
    public IReadOnlyList<ProxyConfigLintUpstream> Upstreams { get; }
    public string StaticResponseBody { get; }
}
