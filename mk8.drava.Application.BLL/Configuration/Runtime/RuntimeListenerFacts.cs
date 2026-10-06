namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeListenerFacts
{
    public static void ValidateIdentity(string name, string address, int port, RuntimeListenerTransport transport)
    {
        ValidateEndpoint(name, address, port);
        _ = RuntimeListenerTransportScheme.FromTransport(transport);
    }

    public static void ValidateQuicIdentity(string name, string address, int port)
    {
        ValidateEndpoint(name, address, port);
    }

    public static void ValidateProjectionIdentity(string name, string address, int port, RuntimeListenerTransport transport, string key, string bindKey)
    {
        ValidateIdentity(name, address, port, transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindKey);
    }

    public static void ValidateQuicProjectionIdentity(string name, string address, int port, string key, string bindKey)
    {
        ValidateQuicIdentity(name, address, port);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindKey);
    }

    public static void Validate(int port, int backlog, int maxRequestHeadBytes, int maxResponseHeadBytes, int maxChunkLineBytes, int forwardingBufferBytes)
    {
        ValidatePort(port);
        if (backlog < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(backlog));
        }

        if (maxRequestHeadBytes is < 1024 or > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequestHeadBytes));
        }

        if (maxResponseHeadBytes is < 1024 or > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResponseHeadBytes));
        }

        if (maxChunkLineBytes is < 64 or > 16 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkLineBytes));
        }

        if (forwardingBufferBytes is < 4096 or > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(forwardingBufferBytes));
        }
    }

    private static void ValidateEndpoint(string name, string address, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ValidatePort(port);
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
    }
}
