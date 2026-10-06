namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeQuicListenerIdentityResponse
{
    public RuntimeQuicListenerIdentityResponse(string name, string address, int port, bool tlsEnabled, string key, string bindKey)
    {
        Name = name;
        Address = address;
        Port = port;
        TlsEnabled = tlsEnabled;
        Key = key;
        BindKey = bindKey;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public bool TlsEnabled { get; }
    public string Key { get; }
    public string BindKey { get; }
}
