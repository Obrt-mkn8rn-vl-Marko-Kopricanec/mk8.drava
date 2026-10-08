namespace Mk8.Drava.Application.INF.Acme;

internal sealed class AcmeOperationHttpClient : IDisposable
{
    private readonly HttpMessageHandler _handler;
    private readonly HttpMessageHandler _inner;
    private readonly CancellationTokenRegistration _cancellation;
    public HttpClient Client { get; }

    public AcmeOperationHttpClient(Uri directory, TimeSpan requestTimeout, CancellationToken operationToken, Func<HttpMessageHandler>? handlerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!directory.IsAbsoluteUri || !string.Equals(directory.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            directory.UserInfo.Length != 0 || directory.Query.Length != 0 || directory.Fragment.Length != 0)
            throw new InvalidDataException("ACME requires an approved HTTPS directory without credentials or redirects.");
        if (requestTimeout < TimeSpan.FromSeconds(1) || requestTimeout > TimeSpan.FromSeconds(30))
            throw new InvalidDataException("ACME request timeout exceeds its supported bounds.");
        operationToken.ThrowIfCancellationRequested();
        HttpMessageHandler? inner = null;
        try
        {
#pragma warning disable CA2000 // Failure finally disposes inner; completed construction transfers it to _inner, disposed by this owner.
            inner = handlerFactory?.Invoke() ?? CreateDefaultHandler();
#pragma warning restore CA2000
            operationToken.ThrowIfCancellationRequested();
            _inner = inner;
            _handler = new OriginHandler(directory.GetLeftPart(UriPartial.Authority), _inner, operationToken);
            Client = new HttpClient(_handler, disposeHandler: false) { Timeout = requestTimeout, MaxResponseContentBufferSize = 256 * 1024 };
            _cancellation = operationToken.Register(static state => ((HttpClient)state!).CancelPendingRequests(), Client);
            inner = null;
        }
        catch { Client?.Dispose(); _handler?.Dispose(); throw; }
        finally { inner?.Dispose(); }
    }

    private static SocketsHttpHandler CreateDefaultHandler()
    {
        var handler = new SocketsHttpHandler();
        try
        {
            handler.AllowAutoRedirect = false;
            handler.MaxConnectionsPerServer = 1;
            return handler;
        }
        catch { handler.Dispose(); throw; }
    }

    public void Dispose()
    {
        _cancellation.Dispose();
        Client.Dispose();
        _handler.Dispose();
        _inner.Dispose();
    }

    private sealed class OriginHandler : HttpMessageHandler
    {
        private readonly string _origin;
        private readonly CancellationToken _operationToken;
        private readonly HttpMessageInvoker _invoker;

        public OriginHandler(string origin, HttpMessageHandler inner, CancellationToken operationToken)
        {
            _origin = origin; _operationToken = operationToken;
            _invoker = new HttpMessageInvoker(inner, disposeHandler: false);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _operationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri;
            if (uri is null || !uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
                uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                !string.Equals(uri.GetLeftPart(UriPartial.Authority), _origin, StringComparison.Ordinal) ||
                request.Method != HttpMethod.Get && request.Method != HttpMethod.Post && request.Method != HttpMethod.Head)
                throw new InvalidDataException("ACME server returned an endpoint outside its approved origin or method scope.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_operationToken, cancellationToken);
            var response = await _invoker.SendAsync(request, linked.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                response.Dispose();
                throw new InvalidDataException("ACME redirects require an explicit owner-approved directory change.");
            }
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _invoker.Dispose();
            base.Dispose(disposing);
        }
    }
}
