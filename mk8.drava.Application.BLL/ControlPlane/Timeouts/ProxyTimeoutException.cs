namespace Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
public sealed class ProxyTimeoutException : TimeoutException
{
    public ProxyTimeoutException(ProxyTimeoutKind kind, TimeSpan timeout) : base($"{kind} timed out after {timeout}.")
    {
        Kind = kind;
        Timeout = timeout;
    }

    public ProxyTimeoutException() : base()
    {
    }

    public ProxyTimeoutException(string? message) : base(message)
    {
    }

    public ProxyTimeoutException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    public ProxyTimeoutKind Kind { get; }
    public TimeSpan Timeout { get; }
}
