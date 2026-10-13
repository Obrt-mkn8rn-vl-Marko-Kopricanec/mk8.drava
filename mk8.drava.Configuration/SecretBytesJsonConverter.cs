using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mk8.Drava.Configuration;

internal sealed class SecretBytesJsonConverter : JsonConverter<SecretBytes>
{
    public override bool HandleNull => true;

    public override SecretBytes Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Protected bytes require a canonical Base64 value.");
        var value = reader.GetString() ?? throw new JsonException("Protected bytes cannot be null.");
        if (value.Length > 4 * ((SecretBytes.MaximumBytes + 2) / 3)) throw new JsonException("Protected bytes exceed their encoding bound.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value); }
        catch (FormatException exception) { throw new JsonException("Protected bytes require a canonical Base64 value.", exception); }
        try
        {
            if (bytes.Length > SecretBytes.MaximumBytes) throw new JsonException("Protected bytes exceed their encoding bound.");
            if (!string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal)) throw new JsonException("Protected bytes require a canonical Base64 value.");
            return new SecretBytes(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public override void Write(Utf8JsonWriter writer, SecretBytes value, JsonSerializerOptions options) => writer.WriteBase64StringValue(value.Value);
}
