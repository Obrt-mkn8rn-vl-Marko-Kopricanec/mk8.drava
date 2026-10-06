namespace Mk8.Drava.Application.BLL.Configuration;
public static class RuntimeListenerTransportScheme
{
    public static string FromTransport(RuntimeListenerTransport transport)
    {
        return transport switch
        {
            RuntimeListenerTransport.Http => "http",
            RuntimeListenerTransport.Https => "https",
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)};
    }
}
