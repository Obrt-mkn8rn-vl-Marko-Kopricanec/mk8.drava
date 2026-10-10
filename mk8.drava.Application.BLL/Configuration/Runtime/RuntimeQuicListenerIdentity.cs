namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeQuicListenerIdentity
{
    public RuntimeQuicListenerIdentity(string Name, string Address, int Port, bool TlsEnabled)
    {
        RuntimeListenerFacts.ValidateQuicIdentity(Name, Address, Port);
        this.Name = Name;
        this.Address = Address;
        this.Port = Port;
        this.TlsEnabled = TlsEnabled;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public bool TlsEnabled { get; }
    public string Key => $"{Normalize(Name)}|quic";
    public string BindKey => $"{Normalize(Address)}|{Port}|udp|quic";

    public static RuntimeQuicListenerIdentity From(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new RuntimeQuicListenerIdentity(listener.Name, listener.Address, listener.Port, listener.Transport == RuntimeListenerTransport.Https);
    }

    private static string Normalize(string value)
    {
        #pragma warning disable CA1308 // QUIC Key/BindKey are externally projected canonical lower-case identities shared with reload/status; preserve the imported identity format.
        return value.Trim().ToLowerInvariant();
        #pragma warning restore CA1308
    }
}
