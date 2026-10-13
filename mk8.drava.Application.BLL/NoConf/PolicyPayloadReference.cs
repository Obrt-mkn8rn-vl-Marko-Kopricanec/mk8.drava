namespace Mk8.Drava.Application.BLL.NoConf;

public sealed record PolicyPayloadReference
{
    public const int MaximumUtf8Bytes = 256 * 1024;

    public PolicyPayloadReference(string digest, int utf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 64 || digest.Any(static character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new InvalidDataException("Policy payload digest must use canonical SHA256.");
        if (utf8Bytes is < 2 or > MaximumUtf8Bytes) throw new InvalidDataException("Policy payload exceeds its durable byte bound.");
        Digest = digest;
        Utf8Bytes = utf8Bytes;
    }

    public string Digest { get; }
    public int Utf8Bytes { get; }
}
