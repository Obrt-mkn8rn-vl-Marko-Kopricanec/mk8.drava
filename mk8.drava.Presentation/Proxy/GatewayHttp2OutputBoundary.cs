using System.Buffers.Binary;

namespace Mk8.Drava.Presentation.Proxy;

internal sealed class GatewayHttp2OutputBoundary
{
    private readonly byte[] _header = new byte[9];
    private int _headerBytes;
    private int _remaining;
    private int _headerStream;
    private bool _started;
    private bool _blockOpen;

    public bool CanInsert => _started && _headerBytes == 0 && _remaining == 0 && !_blockOpen;

    public int Advance(ReadOnlySpan<byte> bytes)
    {
        if (_remaining > 0)
        {
            var consumed = Math.Min(_remaining, bytes.Length);
            _remaining -= consumed;
            if (_remaining == 0) CompleteFrame();
            return consumed;
        }
        var count = Math.Min(bytes.Length, _header.Length - _headerBytes);
        bytes[..count].CopyTo(_header.AsSpan(_headerBytes));
        _headerBytes += count;
        if (_headerBytes == _header.Length)
        {
            _remaining = (_header[0] << 16) | (_header[1] << 8) | _header[2];
            if (_remaining == 0) CompleteFrame();
        }
        return count;
    }

    private void CompleteFrame()
    {
        var type = _header[3];
        var flags = _header[4];
        var stream = BinaryPrimitives.ReadInt32BigEndian(_header.AsSpan(5)) & int.MaxValue;
        if (!_started && (type != 4 || stream != 0)) throw new InvalidDataException("First HTTP/2 output frame is not SETTINGS.");
        if (_blockOpen && (type != 9 || stream != _headerStream)) throw new InvalidDataException("HTTP/2 output interleaves a header block.");
        if (type is 1 or 5 or 9)
        {
            _blockOpen = (flags & 4) == 0;
            _headerStream = stream;
        }
        _started = true;
        _headerBytes = 0;
    }
}
