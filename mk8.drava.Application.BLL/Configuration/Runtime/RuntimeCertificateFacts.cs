namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeCertificateFacts
{
    public static void Validate(string id, string path, string format, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
    }

    public static void ValidateProjection(string id, string path, string format, string source, DateTime notBefore, DateTime notAfter)
    {
        Validate(id, path, format, source);
        if (notAfter < notBefore)
        {
            throw new ArgumentOutOfRangeException(nameof(notAfter), notAfter, "Certificate validity end must not be earlier than its start.");
        }
    }
}
