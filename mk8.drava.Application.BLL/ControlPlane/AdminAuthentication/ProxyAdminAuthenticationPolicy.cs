using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
public static class ProxyAdminAuthenticationPolicy
{
    public const string AdminApiKeyHeaderName = "X-MDRAVA-Admin-Key";
    public const string NotRequiredResult = "not-required";
    public const string NotConfiguredResult = "not-configured";
    public const string MissingResult = "missing";
    public const string InvalidResult = "invalid";
    public const string ValidResult = "valid";
    public static ProxyAdminAuthenticationDecision Authenticate(ProxyAdminAuthenticationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.RequireAuthentication)
        {
            return new ProxyAdminAuthenticationDecision(Allowed: true, AuthenticationRequired: false, NotRequiredResult, ShouldChallenge: false);
        }

        if (string.IsNullOrEmpty(input.ExpectedToken))
        {
            return Deny(NotConfiguredResult, shouldChallenge: false);
        }

        var presentedToken = ReadBearerToken(input.PresentedCredentials.AuthorizationHeaders) ?? ReadApiKey(input.PresentedCredentials.ApiKeyHeaders);
        if (presentedToken is null)
        {
            return Deny(MissingResult, shouldChallenge: true);
        }

        if (!FixedTimeEquals(input.ExpectedToken, presentedToken))
        {
            return Deny(InvalidResult, shouldChallenge: false);
        }

        return new ProxyAdminAuthenticationDecision(Allowed: true, AuthenticationRequired: true, ValidResult, ShouldChallenge: false);
    }

    private static ProxyAdminAuthenticationDecision Deny(string result, bool shouldChallenge)
    {
        return new ProxyAdminAuthenticationDecision(Allowed: false, AuthenticationRequired: true, result, shouldChallenge);
    }

    private static string? ReadBearerToken(IReadOnlyList<string> authorizationValues)
    {
        foreach (var value in authorizationValues)
        {
            const string prefix = "Bearer ";
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = value[prefix.Length..].Trim();
                return token.Length == 0 ? null : token;
            }
        }

        return null;
    }

    private static string? ReadApiKey(IReadOnlyList<string> values)
    {
        var value = values.Count == 0 ? null : values[0];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static bool FixedTimeEquals(string expected, [NotNullWhen(true)] string? actual)
    {
        if (actual is null)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
