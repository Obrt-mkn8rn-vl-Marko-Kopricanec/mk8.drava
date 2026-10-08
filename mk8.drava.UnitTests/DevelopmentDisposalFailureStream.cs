namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentDisposalFailureStream(bool fail) : MemoryStream
{
    public bool Disposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (Disposed) return;
        Disposed = true;
        base.Dispose(disposing);
        if (disposing && fail) throw new IOException("Development owned stream disposal failure.");
    }
}
