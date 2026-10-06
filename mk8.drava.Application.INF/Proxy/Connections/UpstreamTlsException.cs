namespace Mk8.Drava.Application.INF.Proxy.Connections;
public sealed class UpstreamTlsException : IOException
{
    public UpstreamTlsException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public UpstreamTlsException() : base()
    {
    }

    public UpstreamTlsException(string? message) : base(message)
    {
    }

    public UpstreamTlsException(string? message, int hresult) : base(message, hresult)
    {
    }
}
