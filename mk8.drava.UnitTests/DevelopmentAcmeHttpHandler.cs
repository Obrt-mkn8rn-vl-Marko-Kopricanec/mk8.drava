using System.Net;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentAcmeHttpHandler : HttpMessageHandler
{
    private int _requests;
    private int _disposals;
    public int Requests => Volatile.Read(ref _requests);
    public int Disposals => Volatile.Read(ref _disposals);
    public bool BlockHeaders { get; init; }
    public bool Oversize { get; init; }
    public Func<HttpContent>? Content { get; init; }
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        Entered.TrySetResult();
        try
        {
            if (BlockHeaders) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            if (Content is { } factory) return new HttpResponseMessage(Status) { Content = factory() };
            return new HttpResponseMessage(Status) { Content = Oversize ? new UnknownLengthProviderContent() : new StringContent("{}") };
        }
        finally { Exited.TrySetResult(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Interlocked.Increment(ref _disposals);
        base.Dispose(disposing);
    }
}
