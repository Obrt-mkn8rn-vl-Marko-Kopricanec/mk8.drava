namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeQuicListenerIdentityProjection
{
    public RuntimeQuicListenerIdentityProjection(string Name, string Address, int Port, bool TlsEnabled, string Key, string BindKey)
    {
        RuntimeListenerFacts.ValidateQuicProjectionIdentity(Name, Address, Port, Key, BindKey);
        this.Name = Name;
        this.Address = Address;
        this.Port = Port;
        this.TlsEnabled = TlsEnabled;
        this.Key = Key;
        this.BindKey = BindKey;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public bool TlsEnabled { get; }
    public string Key { get; }
    public string BindKey { get; }
}
