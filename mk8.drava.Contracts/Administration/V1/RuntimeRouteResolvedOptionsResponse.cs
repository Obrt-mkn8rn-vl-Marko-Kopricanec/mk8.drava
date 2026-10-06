namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeRouteResolvedOptionsResponse(long MaxRequestBodyBytes, TimeSpan ClientRequestHeadTimeout, TimeSpan UpstreamResponseHeadTimeout, bool AccessLogEnabled)
{
}
