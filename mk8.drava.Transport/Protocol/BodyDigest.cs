using System.Security.Cryptography;
using Google.Protobuf;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Protocol;

public sealed class BodyDigest : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private ulong _bytes;
    private bool _completed;

    public ulong Bytes => _bytes;

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_completed) throw new InvalidOperationException("The body already completed.");
        _hash.AppendData(data);
        _bytes = checked(_bytes + (ulong)data.Length);
    }

    public Completion Complete()
    {
        if (_completed) throw new InvalidOperationException("Duplicate body completion.");
        _completed = true;
        return new Completion { BodyBytes = _bytes, Sha256 = ByteString.CopyFrom(_hash.GetHashAndReset()) };
    }

    public void Verify(Completion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (_completed) throw new InvalidDataException("Duplicate body completion.");
        _completed = true;
        var hash = _hash.GetHashAndReset();
        if (completion.BodyBytes != _bytes || completion.Sha256.Length != 32 || !CryptographicOperations.FixedTimeEquals(hash, completion.Sha256.Span)) throw new InvalidDataException("Body completion length or digest mismatch.");
    }

    public void Dispose() => _hash.Dispose();
}
