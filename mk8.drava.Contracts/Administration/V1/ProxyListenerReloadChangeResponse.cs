namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyListenerReloadChangeResponse(string Action, string Name, string Identity, string BindKey, string State, string? Error)
{
}
