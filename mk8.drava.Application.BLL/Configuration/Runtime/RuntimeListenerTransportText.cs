namespace Mk8.Drava.Application.BLL.Configuration;
public static class RuntimeListenerTransportText
{
    public static string FromTransport(RuntimeListenerTransport transport)
    {
        return transport switch
        {
            RuntimeListenerTransport.Http => nameof(RuntimeListenerTransport.Http),
            RuntimeListenerTransport.Https => nameof(RuntimeListenerTransport.Https),
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)};
    }
}
