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

    public static async Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > MaximumBytes) throw new InvalidDataException("Bootstrap file is absent or exceeds its size bound.");
        var bytes = new byte[MaximumBytes + 1];
        var input = File.OpenRead(path);
        await using var inputLifetime = input.ConfigureAwait(false);
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await input.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        if (count > MaximumBytes) throw new InvalidDataException("Bootstrap file exceeds its size bound.");
        return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count), Options) ?? throw new InvalidDataException("Bootstrap file must contain an object.");
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
