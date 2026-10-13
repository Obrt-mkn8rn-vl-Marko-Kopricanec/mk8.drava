using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mk8.Drava.Configuration;

internal sealed class SecretTextJsonConverter : JsonConverter<SecretText>
{
    public SecretTextJsonConverter() { }
    public override bool HandleNull => true;

    public override SecretText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Secret configuration requires a string.");
        return new SecretText(reader.GetString() ?? throw new JsonException("Secret configuration requires a string."));
    }

    public override void Write(Utf8JsonWriter writer, SecretText value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Reveal());
    }
}
