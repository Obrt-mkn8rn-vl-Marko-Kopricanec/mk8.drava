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
        return value.Trim().ToLowerInvariant();
    }
}
