namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyMetricsOptions
{
    public bool Enabled { get; init; } = true;
    public bool IncludePerRouteLabels { get; init; } = true;
    public bool IncludePerUpstreamLabels { get; init; }
    public bool PublicMetricsEnabled { get; init; }
}
