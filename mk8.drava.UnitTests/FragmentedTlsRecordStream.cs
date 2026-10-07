namespace Mk8.Drava.UnitTests;

// Development-only peer: coalesce one complete TLS record with the chosen prefix of the next,
// then withhold its remainder. The framework still encrypts and authenticates both records.
internal sealed class FragmentedTlsRecordStream(Stream inner, int prefixBytes) : Stream
{
    private bool _armed;
    private bool _fragmented;
    private byte[]? _first;
    public void Arm()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(prefixBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixBytes, 6);
        _armed = true;
    }
    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_armed) throw new InvalidDataException("Development fragmented TLS peer requires asynchronous writes.");
        inner.Write(buffer);
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_armed) { await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false); return; }
        if (_fragmented) return; // Deliberately withhold all remaining peer output.
        if (buffer.Length < 6 || buffer.Length > 1024 || buffer.Span[0] != 23)
            throw new InvalidDataException("Development fragment requires one bounded encrypted application record.");
        if (_first is null) { _first = buffer.ToArray(); return; }
        var combined = new byte[_first.Length + prefixBytes];
        _first.CopyTo(combined, 0);
        buffer.Span[..prefixBytes].CopyTo(combined.AsSpan(_first.Length));
        _first = null;
        _fragmented = true;
        await inner.WriteAsync(combined, cancellationToken).ConfigureAwait(false);
    }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    // The owner holds the TcpClient; this wrapper borrows its stream.
}
