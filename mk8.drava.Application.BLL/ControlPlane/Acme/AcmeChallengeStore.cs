namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed class AcmeChallengeStore
{
    public const int MaximumTokenLength = 256;
    public const int MaximumResponseBodyLength = 4096;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AcmeChallengeRegistration> _challenges = new(StringComparer.Ordinal);
    public AcmeChallengeRegistrationResult Register(string token, string responseBody, DateTimeOffset expiresAtUtc)
    {
        if (!IsValidToken(token))
        {
            return AcmeChallengeRegistrationResult.Rejected("invalid-token");
        }

        if (string.IsNullOrEmpty(responseBody) || responseBody.Length > MaximumResponseBodyLength)
        {
            return AcmeChallengeRegistrationResult.Rejected("invalid-response-body");
        }

        lock (_gate)
        {
            _challenges[token] = new AcmeChallengeRegistration(token, responseBody, expiresAtUtc);
            return AcmeChallengeRegistrationResult.Registered;
        }
    }

    public AcmeChallengeResponseLookupResult FindResponse(string token, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (_challenges.TryGetValue(token, out var challenge))
            {
                if (challenge.ExpiresAtUtc > nowUtc)
                {
                    return AcmeChallengeResponseLookupResult.Found(challenge.ResponseBody);
                }

                _challenges.Remove(token);
            }
        }

        return AcmeChallengeResponseLookupResult.Missing;
    }

    public bool Remove(string token)
    {
        lock (_gate)
        {
            return _challenges.Remove(token);
        }
    }

    public static bool IsValidToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Length is 0 or > MaximumTokenLength)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }
}
