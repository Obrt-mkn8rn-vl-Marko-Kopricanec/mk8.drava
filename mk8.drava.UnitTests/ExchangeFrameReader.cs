using Grpc.Core;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.UnitTests;

internal sealed class ExchangeFrameReader(IEnumerable<ExchangeFrame> frames) : IAsyncStreamReader<ExchangeFrame>, IDisposable
{
    private readonly IEnumerator<ExchangeFrame> _frames = frames.GetEnumerator();
    public ExchangeFrame Current => _frames.Current;
    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_frames.MoveNext());
    }
    public void Dispose() => _frames.Dispose();
}
