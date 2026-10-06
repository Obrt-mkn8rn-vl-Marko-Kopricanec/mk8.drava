namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyRouteOverrideOptions
{
    public long? MaxRequestBodyBytes { get; init; }
    public int? ClientRequestHeadTimeoutMs { get; init; }
    public int? UpstreamResponseHeadTimeoutMs { get; init; }
    public bool? AccessLogEnabled { get; init; }
}
