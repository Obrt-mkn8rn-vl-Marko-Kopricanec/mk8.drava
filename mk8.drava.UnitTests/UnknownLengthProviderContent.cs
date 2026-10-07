using System.Net;

namespace Mk8.Drava.UnitTests;

internal sealed class UnknownLengthProviderContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(new byte[256 * 1024 + 1]).AsTask();
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
}
