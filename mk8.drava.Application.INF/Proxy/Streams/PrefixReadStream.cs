namespace Mk8.Drava.Application.INF.Proxy.Streams;
internal sealed class PrefixReadStream(Stream inner, ReadOnlyMemory<byte> prefix) : Stream
{
    private ReadOnlyMemory<byte> _remaining = prefix;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        if (_remaining.IsEmpty)
            return inner.Read(buffer);
        return ReadPrefix(buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _remaining.IsEmpty ? inner.ReadAsync(buffer, cancellationToken) : ValueTask.FromResult(ReadPrefix(buffer.Span));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    private int ReadPrefix(Span<byte> buffer)
    {
        var length = Math.Min(_remaining.Length, buffer.Length);
        _remaining.Span[..length].CopyTo(buffer);
        _remaining = _remaining[length..];
        return length;
    }
}
