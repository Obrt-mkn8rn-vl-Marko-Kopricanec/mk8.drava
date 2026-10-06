namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeListenerIdentityResponse
{
    public RuntimeListenerIdentityResponse(string name, string address, int port, RuntimeListenerTransportResponse transport, bool tlsEnabled, string key, string bindKey)
    {
        Name = name;
        Address = address;
        Port = port;
        Transport = transport;
        TlsEnabled = tlsEnabled;
        Key = key;
        BindKey = bindKey;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public RuntimeListenerTransportResponse Transport { get; }
    public bool TlsEnabled { get; }
    public string Key { get; }
    public string BindKey { get; }
}
