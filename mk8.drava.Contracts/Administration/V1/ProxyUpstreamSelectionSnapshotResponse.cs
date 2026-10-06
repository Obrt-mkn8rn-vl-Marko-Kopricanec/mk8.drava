namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyUpstreamSelectionSnapshotResponse(string Route, string Upstream, string Scheme, string Protocol, long Count)
{
}
