namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RouteMatchDryRunListenerResponse(string Name, string Transport, string Address, int Port, string Protocols)
{
}
