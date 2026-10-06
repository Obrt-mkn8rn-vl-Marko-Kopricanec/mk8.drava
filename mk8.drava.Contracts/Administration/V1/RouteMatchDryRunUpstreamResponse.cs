namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RouteMatchDryRunUpstreamResponse(string Name, string Scheme, string Protocol, string Endpoint, int Weight, string SelectionReason)
{
}
