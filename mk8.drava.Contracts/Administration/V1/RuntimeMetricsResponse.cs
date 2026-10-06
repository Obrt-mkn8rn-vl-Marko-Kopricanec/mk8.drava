namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeMetricsResponse(bool Enabled, string EndpointPath, bool ProtectedByAdminAuth, bool IncludePerRouteLabels, bool IncludePerUpstreamLabels, bool PublicMetricsEnabled)
{
}
