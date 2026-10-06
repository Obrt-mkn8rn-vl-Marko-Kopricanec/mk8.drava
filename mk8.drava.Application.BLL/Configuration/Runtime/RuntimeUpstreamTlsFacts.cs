using System.Net;

namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeUpstreamTlsFacts
{
    public static void Validate(string? sniHost)
    {
        if (sniHost is null)
        {
            return;
        }

        var trimmed = sniHost.Trim();
        if (!string.Equals(sniHost, trimmed, StringComparison.Ordinal) || trimmed.Length is 0 or > 253 || trimmed.StartsWith("*.", StringComparison.Ordinal) || trimmed.Contains('/', StringComparison.Ordinal) || trimmed.Contains('\\', StringComparison.Ordinal) || trimmed.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new ArgumentException("SNI host must be a normalized DNS host name or IP literal.", nameof(sniHost));
        }

        if (IPAddress.TryParse(trimmed, out _))
        {
            return;
        }

        if (trimmed.Contains(':', StringComparison.Ordinal) || Uri.CheckHostName(trimmed)is not (UriHostNameType.Dns or UriHostNameType.IPv4))
        {
            throw new ArgumentException("SNI host must be a normalized DNS host name or IP literal.", nameof(sniHost));
        }
    }
}
