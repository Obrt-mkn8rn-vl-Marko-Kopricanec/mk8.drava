using System.Net;
using System.Security.Cryptography;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentRepeatedBodyContent(long length) : HttpContent
{
    private const int BufferBytes = 32 * 1024;
    private long _serializedBytes;
    public bool SerializationStarted { get; private set; }
    public long SerializedBytes => Interlocked.Read(ref _serializedBytes);

    protected override bool TryComputeLength(out long value) { value = length; return true; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        SerializationStarted = true;
        var buffer = CreateBuffer();
        for (var remaining = length; remaining > 0;)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _serializedBytes, count);
            remaining -= count;
        }
    }

    public static string ExpectedHash(long count)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = CreateBuffer();
        for (var remaining = count; remaining > 0;)
        {
            var size = (int)Math.Min(buffer.Length, remaining);
            hash.AppendData(buffer.AsSpan(0, size));
            remaining -= size;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static byte[] CreateBuffer()
    {
        var buffer = new byte[BufferBytes];
        for (var index = 0; index < buffer.Length; index++) buffer[index] = (byte)(index % 251);
        return buffer;
    }
}
