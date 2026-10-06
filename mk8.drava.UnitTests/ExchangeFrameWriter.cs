using System.Collections.ObjectModel;
using Grpc.Core;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.UnitTests;

internal sealed class ExchangeFrameWriter : IServerStreamWriter<ExchangeFrame>
{
    public Collection<ExchangeFrame> Frames { get; } = [];
    public WriteOptions? WriteOptions { get; set; }
    public Task WriteAsync(ExchangeFrame message) { Frames.Add(message); return Task.CompletedTask; }
    public Task WriteAsync(ExchangeFrame message, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return WriteAsync(message); }
}
