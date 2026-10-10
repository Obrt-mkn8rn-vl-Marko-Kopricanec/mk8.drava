namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeListenerIdentity
{
    public RuntimeListenerIdentity(string Name, string Address, int Port, RuntimeListenerTransport Transport, bool TlsEnabled)
    {
        RuntimeListenerFacts.ValidateIdentity(Name, Address, Port, Transport);
        this.Name = Name;
        this.Address = Address;
        this.Port = Port;
        this.Transport = Transport;
        this.TlsEnabled = TlsEnabled;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public RuntimeListenerTransport Transport { get; }
    public bool TlsEnabled { get; }
    public string Key => Normalize(Name);
    public string BindKey => $"{Normalize(Address)}|{Port}|{RuntimeListenerTransportScheme.FromTransport(Transport)}";

    public static RuntimeListenerIdentity From(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new RuntimeListenerIdentity(listener.Name, listener.Address, listener.Port, listener.Transport, listener.Transport == RuntimeListenerTransport.Https);
    }

    private static string Normalize(string value)
    {
        #pragma warning disable CA1308 // Listener Key/BindKey are externally projected canonical lower-case identities; changing their representation would alter reload/status identities.
        return value.Trim().ToLowerInvariant();
        #pragma warning restore CA1308
    }
}
