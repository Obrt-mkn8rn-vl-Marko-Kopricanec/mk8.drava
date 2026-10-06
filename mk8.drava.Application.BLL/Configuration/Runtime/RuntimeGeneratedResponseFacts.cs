using System.Text;

namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeGeneratedResponseFacts
{
    private const int MaxGeneratedBodyBytes = 64 * 1024;
    public static void ValidateStaticResponse(int statusCode, string contentType, string body)
    {
        ValidateStatusCode(statusCode);
        ValidateContentType(contentType);
        ValidateBody(body);
    }

    public static void ValidateMaintenance(int? retryAfterSeconds, string contentType, string body)
    {
        if (retryAfterSeconds is < 0 or > 86400)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfterSeconds));
        }

        ValidateContentType(contentType);
        ValidateBody(body);
    }

    private static void ValidateStatusCode(int statusCode)
    {
        if (statusCode is < 200 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }
    }

    private static void ValidateContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Any(static character => character is '\r' or '\n'))
        {
            throw new ArgumentException("Generated response content type must be a non-empty single-line value.", nameof(contentType));
        }
    }

    private static void ValidateBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (Encoding.UTF8.GetByteCount(body) > MaxGeneratedBodyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(body));
        }
    }
}
