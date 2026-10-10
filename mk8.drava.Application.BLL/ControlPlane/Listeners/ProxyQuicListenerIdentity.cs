namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed record ProxyQuicListenerIdentity(string Name, string Address, int Port, bool TlsEnabled)
{
    public string Key => $"{Normalize(Name)}|quic";
    public string BindKey => $"{Normalize(Address)}|{Port}|udp|quic";

    private static string Normalize(string value)
    {
        #pragma warning disable CA1308 // This public status identity shares the exact canonical lower-case QUIC key format with RuntimeQuicListenerIdentity.
        return value.Trim().ToLowerInvariant();
        #pragma warning restore CA1308
    }
}
