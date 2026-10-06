using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Transport;

internal sealed class IpcIdentityInterceptor : Interceptor
{
    private readonly byte[] _tokenHash;

    public IpcIdentityInterceptor(ApplicationBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        var file = new FileInfo(bootstrap.Listen.IdentityTokenPath);
        if (!file.Exists || file.Length is < 32 or > 256) throw new InvalidDataException("Invalid scoped IPC identity file.");
        var token = File.ReadAllText(file.FullName).Trim();
        if (token.Length is < 32 or > 256 || token.Any(static value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
            throw new InvalidDataException("Invalid scoped IPC identity encoding.");
        _tokenHash = SHA256.HashData(Encoding.ASCII.GetBytes(token));
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);
        Authenticate(context);
        return continuation(request, context);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);
        Authenticate(context);
        return continuation(requestStream, responseStream, context);
    }

    private void Authenticate(ServerCallContext context)
    {
        var entries = context.RequestHeaders.Where(static entry => string.Equals(entry.Key, "authorization", StringComparison.Ordinal)).ToArray();
        if (entries.Length != 1 || !entries[0].Value.StartsWith("Bearer ", StringComparison.Ordinal) || entries[0].Value.Length is < 39 or > 263)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Scoped transport identity required."));
        var supplied = SHA256.HashData(Encoding.ASCII.GetBytes(entries[0].Value.AsSpan(7).ToString()));
        if (!CryptographicOperations.FixedTimeEquals(supplied, _tokenHash))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid scoped transport identity."));
    }
}
