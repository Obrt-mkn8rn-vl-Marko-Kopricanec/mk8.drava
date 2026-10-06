using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public static class ProxyExternalRequestIdPolicy
{
    private const int MaxLength = 128;
    public static string? Extract(Http1RequestHead requestHead)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        foreach (var header in requestHead.Headers)
        {
            if (string.Equals(header.Name, "X-Request-Id", StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(header.Value);
            }
        }

        return null;
    }

    public static string? Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim();
        if (normalized.Length is 0 or > MaxLength)
        {
            return null;
        }

        foreach (var character in normalized)
        {
            if (char.IsControl(character))
            {
                return null;
            }
        }

        return normalized;
    }
}
