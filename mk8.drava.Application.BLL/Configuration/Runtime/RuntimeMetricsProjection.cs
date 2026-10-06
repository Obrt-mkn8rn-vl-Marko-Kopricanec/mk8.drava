namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeMetricsProjection
{
    public const string FixedAdminEndpointPath = "/admin/proxy/metrics";
    public RuntimeMetricsProjection(bool Enabled, string EndpointPath, bool ProtectedByAdminAuth, bool IncludePerRouteLabels, bool IncludePerUpstreamLabels, bool PublicMetricsEnabled)
    {
        RuntimeMetricsFacts.ValidateEndpointPath(EndpointPath, nameof(EndpointPath));
        this.Enabled = Enabled;
        this.EndpointPath = EndpointPath;
        this.ProtectedByAdminAuth = ProtectedByAdminAuth;
        this.IncludePerRouteLabels = IncludePerRouteLabels;
        this.IncludePerUpstreamLabels = IncludePerUpstreamLabels;
        this.PublicMetricsEnabled = PublicMetricsEnabled;
    }

    public bool Enabled { get; }
    public string EndpointPath { get; }
    public bool ProtectedByAdminAuth { get; }
    public bool IncludePerRouteLabels { get; }
    public bool IncludePerUpstreamLabels { get; }
    public bool PublicMetricsEnabled { get; }
    public static RuntimeMetricsProjection Default { get; } = new(true, FixedAdminEndpointPath, true, true, false, false);
}
