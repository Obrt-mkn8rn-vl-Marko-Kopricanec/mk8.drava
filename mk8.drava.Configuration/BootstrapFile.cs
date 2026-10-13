using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mk8.Drava.Configuration;

public static class BootstrapFile
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        RespectNullableAnnotations = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        if (!string.Equals(path, PortableConfiguration.StartupPath([]), StringComparison.Ordinal))
            throw new InvalidDataException("Separate bootstrap files are unsupported; use the installation-local Supprocom collection.");
        return PortableConfiguration.LoadAsync<T>(cancellationToken);
    }

    public static T Parse<T>(ReadOnlyMemory<byte> bytes) where T : class
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Bootstrap file exceeds its size bound.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        RequireUniqueProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(Options) ?? throw new InvalidDataException("Bootstrap file must contain an object.");
    }

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate bootstrap property.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RequireUniqueProperties(item);
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
