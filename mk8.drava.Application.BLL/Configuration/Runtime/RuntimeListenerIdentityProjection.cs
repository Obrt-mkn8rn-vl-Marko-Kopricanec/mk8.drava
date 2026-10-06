namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeListenerIdentityProjection
{
    public RuntimeListenerIdentityProjection(string Name, string Address, int Port, RuntimeListenerTransport Transport, bool TlsEnabled, string Key, string BindKey)
    {
        RuntimeListenerFacts.ValidateProjectionIdentity(Name, Address, Port, Transport, Key, BindKey);
        this.Name = Name;
        this.Address = Address;
        this.Port = Port;
        this.Transport = Transport;
        this.TlsEnabled = TlsEnabled;
        this.Key = Key;
        this.BindKey = BindKey;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public RuntimeListenerTransport Transport { get; }
    public bool TlsEnabled { get; }
    public string Key { get; }
    public string BindKey { get; }
}
