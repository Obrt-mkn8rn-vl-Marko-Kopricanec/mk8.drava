namespace Mk8.Drava.UnitTests;

// A write can commit a prefix before reporting failure. Later writes stay observable to detect a second response.
internal sealed class DevelopmentPartialHeadStream(bool failFirstWrite) : MemoryStream
{
    private bool _failed;

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (failFirstWrite && !_failed)
        {
            _failed = true;
            await base.WriteAsync(buffer[..Math.Min(buffer.Length, 16)], cancellationToken).ConfigureAwait(false);
            throw new IOException("Controlled partial response-head write.");
        }
        await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
}
