using System.Text.Json;
using System.Text.Json.Serialization;
using Mk8.Drava.Application.BLL.NoConf;

namespace Mk8.Drava.Application.DAL.NoConf;

public static class NoConfPolicyFile
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32, RespectNullableAnnotations = true };

    public static async ValueTask<NoConfPolicy> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Policy path must be absolute.");
        if (!File.Exists(path)) return new NoConfPolicy();
        var file = new FileInfo(path);
        if (file.LinkTarget is not null || file.Length is < 2 or > 256 * 1024) throw new InvalidDataException("Policy file exceeds its bounds or is a symbolic link.");
        var bytes = new byte[256 * 1024 + 1];
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = await input.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        if (length == bytes.Length) throw new InvalidDataException("Policy file exceeds its bounds.");
        using var document = JsonDocument.Parse(bytes.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 32 });
        ValidateProperties(document.RootElement);
        return document.RootElement.Deserialize<NoConfPolicy>(Options) ?? throw new InvalidDataException("Policy is missing.");
    }

    private static void ValidateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate policy property.");
                ValidateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null) throw new InvalidDataException("Policy collections cannot contain null entries.");
                ValidateProperties(item);
            }
    }
}
