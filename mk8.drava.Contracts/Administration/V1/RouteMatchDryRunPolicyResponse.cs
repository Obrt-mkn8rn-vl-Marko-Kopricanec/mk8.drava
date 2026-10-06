namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RouteMatchDryRunPolicyResponse(bool Enabled, bool WouldApply, string Reason)
{
}
