using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Administration;

internal static class ControlJson
{
    private const int MaximumReplyBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
    };

    public static T Read<T>(ByteString payload) where T : class
    {
        if (payload.Length is < 1 or > 256 * 1024) throw new InvalidDataException("Control payload size is invalid.");
        using var document = JsonDocument.Parse(payload.Memory, new JsonDocumentOptions { MaxDepth = 32 });
        RequireUniqueProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(Options) ?? throw new InvalidDataException("Control payload is missing.");
    }

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate control property.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RequireUniqueProperties(child);
    }

    public static ControlReply Reply<T>(T response, bool succeeded = true)
    {
        using var stream = new ReplyStream();
        JsonSerializer.Serialize(stream, response, Options);
        return new ControlReply { StatusCode = succeeded ? 200u : 400u, ContentType = "application/json", JsonPayload = ByteString.CopyFrom(stream.GetBuffer(), 0, checked((int)stream.Length)) };
    }

    private sealed class ReplyStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { Validate(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Validate(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Validate(1); base.WriteByte(value); }
        private void Validate(int count)
        {
            if (Length + count > MaximumReplyBytes) throw new InvalidDataException("Control reply exceeds its bound.");
        }
    }
}
