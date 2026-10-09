using System.Net;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentSlowKnownLengthContent : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 3; return true; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var body = "abc"u8.ToArray();
        for (var index = 0; index < body.Length; index++)
        {
            if (index > 0) await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body.AsMemory(index, 1), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
