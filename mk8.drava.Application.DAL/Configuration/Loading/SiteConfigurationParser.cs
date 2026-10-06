using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;
public sealed class SiteConfigurationParser
{
    public static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    public static readonly JsonSerializerOptions WriteJsonOptions = new(ReadJsonOptions)
    {
        WriteIndented = true
    };
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().WithDuplicateKeyChecking().WithAttemptingUnquotedStringTypeDeserialization().Build();
    private static readonly ISerializer YamlJsonSerializer = new SerializerBuilder().JsonCompatible().Build();
    public async ValueTask<SiteOptions?> ReadSiteFileAsync(string path, SiteConfigurationFormat format, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return ReadSiteText(text, format);
    }

    public SiteOptions? ReadSiteText(string text, SiteConfigurationFormat format)
    {
        var json = format == SiteConfigurationFormat.Json ? text : ConvertYamlToJson(text);
        RejectRemovedHttp3Properties(json);
        return JsonSerializer.Deserialize<SiteOptions>(json, ReadJsonOptions);
    }

    public string NormalizeSiteText(string text, SiteConfigurationFormat format)
    {
        var site = ReadSiteText(text, format) ?? throw new JsonException("Site configuration did not contain a JSON object.");
        return JsonSerializer.Serialize(site, WriteJsonOptions);
    }

    private static string ConvertYamlToJson(string yaml)
    {
        var yamlObject = YamlDeserializer.Deserialize<object?>(yaml);
        if (yamlObject is null)
        {
            throw new YamlException("YAML did not contain a document.");
        }

        return YamlJsonSerializer.Serialize(yamlObject);
    }

    private static void RejectRemovedHttp3Properties(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        RejectRemovedHttp3Properties(document.RootElement, "$");
    }

    private static void RejectRemovedHttp3Properties(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = $"{path}.{property.Name}";
                    if (string.Equals(property.Name, "experimentalHttp3", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new JsonException($"Unsupported HTTP/3 configuration property 'experimentalHttp3' at {childPath}; remove it and use the current listener protocol spellings.");
                    }

                    if (string.Equals(property.Name, "http3MaxBufferedRequestBodyBytes", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new JsonException($"Unsupported HTTP/3 configuration property 'http3MaxBufferedRequestBodyBytes' at {childPath}; remove it because HTTP/3 request bodies stream.");
                    }

                    RejectRemovedHttp3Properties(property.Value, childPath);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    RejectRemovedHttp3Properties(item, $"{path}[{index}]");
                    index++;
                }

                break;
        }
    }
}
