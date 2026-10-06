using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
public sealed record ProxyAdminPresentedCredentials
{
    public ProxyAdminPresentedCredentials(IReadOnlyList<string> authorizationHeaders, IReadOnlyList<string> apiKeyHeaders)
    {
        AuthorizationHeaders = CopyValidated(authorizationHeaders, nameof(authorizationHeaders));
        ApiKeyHeaders = CopyValidated(apiKeyHeaders, nameof(apiKeyHeaders));
    }

    public IReadOnlyList<string> AuthorizationHeaders { get; }
    public IReadOnlyList<string> ApiKeyHeaders { get; }

    public static ProxyAdminPresentedCredentials FromRawHeaders(IEnumerable<string?> authorizationHeaders, IEnumerable<string?> apiKeyHeaders)
    {
        return new ProxyAdminPresentedCredentials(CopyNonNull(authorizationHeaders, nameof(authorizationHeaders)), CopyNonNull(apiKeyHeaders, nameof(apiKeyHeaders)));
    }

    private static IReadOnlyList<string> CopyValidated(IReadOnlyList<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (value is null)
            {
                throw new ArgumentException("Header values cannot contain null entries.", parameterName);
            }

            copy.Add(value);
        }

        return copy;
    }

    private static IReadOnlyList<string> CopyNonNull(IEnumerable<string?> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new List<string>();
        foreach (var value in values)
        {
            if (value is not null)
            {
                copy.Add(value);
            }
        }

        return copy;
    }
}
