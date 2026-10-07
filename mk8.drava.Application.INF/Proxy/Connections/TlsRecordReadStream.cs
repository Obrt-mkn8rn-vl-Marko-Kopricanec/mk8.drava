using System.Buffers.Binary;

namespace Mk8.Drava.Application.INF.Proxy.Connections;

// Observe only byte boundaries below the framework TLS implementation. Neither
// ciphertext nor handshake records are modified, buffered, decrypted or validated here.
internal sealed class TlsRecordReadStream(Stream inner) : Stream
{
    private readonly byte[] _header = new byte[5];
    private int _headerBytes;
    private int _payloadBytesRemaining;
    private bool _disposed;
    public bool HasPartialRecord => _headerBytes != 0 || _payloadBytesRemaining != 0;
    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Observe(buffer[..read]);
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Observe(buffer.Span[..read]);
        return read;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && !_disposed) { _disposed = true; inner.Dispose(); }
        }
        finally { base.Dispose(disposing); }
    }
    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (!_disposed) { _disposed = true; await inner.DisposeAsync().ConfigureAwait(false); }
        }
        finally { await base.DisposeAsync().ConfigureAwait(false); }
    }

    private void Observe(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (_payloadBytesRemaining != 0)
            {
                var take = Math.Min(_payloadBytesRemaining, bytes.Length);
                _payloadBytesRemaining -= take;
                bytes = bytes[take..];
                continue;
            }
            var count = Math.Min(5 - _headerBytes, bytes.Length);
            bytes[..count].CopyTo(_header.AsSpan(_headerBytes));
            _headerBytes += count;
            bytes = bytes[count..];
            if (_headerBytes != 5) continue;
            _payloadBytesRemaining = BinaryPrimitives.ReadUInt16BigEndian(_header.AsSpan(3));
            _headerBytes = 0;
        }
    }
}
