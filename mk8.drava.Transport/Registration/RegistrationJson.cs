using System.Text.Json;
using System.Text.Json.Serialization;
using Mk8.Drava.Contracts.Registration.V1;

namespace Mk8.Drava.Transport.Registration;

public static class RegistrationJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    public static byte[] Encode(RegistrationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(command, Options);
        if (bytes.Length > RegistrationProof.MaximumPayloadBytes) throw new InvalidDataException("Registration metadata exceeds its limit.");
        return bytes;
    }

    public static RegistrationCommand Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 1 or > RegistrationProof.MaximumPayloadBytes) throw new InvalidDataException("Invalid registration metadata size.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        RequireUniqueProperties(document.RootElement);
        return document.RootElement.Deserialize<RegistrationCommand>(Options) ?? throw new InvalidDataException("Registration command is missing.");
    }

    public static byte[] EncodeStatus(RegistrationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return JsonSerializer.SerializeToUtf8Bytes(status, Options);
    }

    public static RegistrationStatus DecodeStatus(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > RegistrationProof.MaximumPayloadBytes) throw new InvalidDataException("Invalid registration status size.");
        return JsonSerializer.Deserialize<RegistrationStatus>(bytes, Options) ?? throw new InvalidDataException("Registration status is missing.");
    }

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate registration property.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RequireUniqueProperties(child);
    }
}
