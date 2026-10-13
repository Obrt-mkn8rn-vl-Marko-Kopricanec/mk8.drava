using System.Text;
using Supprocom.Secrets;

namespace Mk8.Drava.Configuration;

internal static class InstallationCollectionBounds
{
    internal const int MaximumDocumentBytes = 32 * 1024 * 1024;
    internal const int MaximumPolicyArchiveBytes = 24 * 1024 * 1024;
    internal const int MaximumPolicySourceBytes = 512 * 1024;
    internal const string PolicyArchiveKey = "Drava:PolicyArchive:ApplicationBootstrap";
    internal const string PolicySourceKey = "Drava:PolicySource:ApplicationBootstrap";
    private const int ProtectionBytes = 29; // Pinned package: version1, nonce12, tag16.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static void RequireValue(string value, int maximumBytes)
    {
        try
        {
            if (value.Length > maximumBytes || StrictUtf8.GetByteCount(value) > maximumBytes)
                throw new InvalidDataException("Protected policy document exceeds its slot bound.");
        }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Protected policy document requires valid UTF8.", exception); }
    }

    internal static void RequireSettings(IReadOnlyList<SupprocomSecretSetting> settings)
    {
        var ordinary = new List<SupprocomSecretSetting>();
        foreach (var setting in settings)
        {
            if (string.Equals(setting.Key, PolicyArchiveKey, StringComparison.OrdinalIgnoreCase))
                RequireValue(setting.Value, MaximumPolicyArchiveBytes);
            else if (string.Equals(setting.Key, PolicySourceKey, StringComparison.OrdinalIgnoreCase))
                RequireValue(setting.Value, MaximumPolicySourceBytes);
            else ordinary.Add(setting);
        }
        RequireSerialized(ordinary, 4 * 1024 * 1024);
        RequireSerialized(settings, MaximumDocumentBytes);
    }

    private static void RequireSerialized(IReadOnlyList<SupprocomSecretSetting> settings, int maximumBytes)
    {
        var canonical = SupprocomSecretDocument.Serialize(settings);
        if (StrictUtf8.GetByteCount(canonical) > maximumBytes - ProtectionBytes)
            throw new InvalidDataException("Protected collection exceeds its serialized document quota.");
    }
}
