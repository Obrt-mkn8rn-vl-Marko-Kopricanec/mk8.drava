namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed record ProxyQuicListenerIdentity(string Name, string Address, int Port, bool TlsEnabled)
{
    public string Key => $"{Normalize(Name)}|quic";
    public string BindKey => $"{Normalize(Address)}|{Port}|udp|quic";

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
