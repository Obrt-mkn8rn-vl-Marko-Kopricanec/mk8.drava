namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyQuicListenerIdentityResponse(string Name, string Address, int Port, bool TlsEnabled, string Key, string BindKey)
{
}
