using System.Net;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ProxyAdminAuthenticationPolicyTests
{
    public static void RequestInputMapperReadsRawRequestFacts()
    {
        var authorizationHeaders = new string? []
        {
            null,
            "Bearer secret"
        };
        var apiKeyHeaders = new[]
        {
            " api-secret "
        };
        var input = ProxyAdminRequestAuthenticationInputMapper.FromRawRequestFacts("POST", null, IPAddress.Parse("::ffff:127.0.0.1"), authorizationHeaders, apiKeyHeaders);
        authorizationHeaders[1] = "Bearer replacement";
        apiKeyHeaders[0] = "replacement";
        AssertEx.Equal("POST", input.Method);
        AssertEx.Equal("/", input.Path);
        AssertEx.Equal("127.0.0.1", input.RemoteClientAddress);
        AssertEx.Equal(1, input.PresentedCredentials.AuthorizationHeaders.Count);
        AssertEx.Equal("Bearer secret", input.PresentedCredentials.AuthorizationHeaders[0]);
        AssertEx.Equal(" api-secret ", input.PresentedCredentials.ApiKeyHeaders[0]);
    }

    public static void ClassifiesAdminAuthenticationAttempts()
    {
        var notRequired = Authenticate(requireAuthentication: false, expectedToken: null, authorizationHeaders: [], apiKeyHeaders: []);
        AssertEx.True(notRequired.Allowed);
        AssertEx.False(notRequired.AuthenticationRequired);
        AssertEx.Equal(ProxyAdminAuthenticationPolicy.NotRequiredResult, notRequired.Result);
        var notConfigured = Authenticate(requireAuthentication: true, expectedToken: null, authorizationHeaders: [], apiKeyHeaders: []);
        AssertEx.False(notConfigured.Allowed);
        AssertEx.Equal(ProxyAdminAuthenticationPolicy.NotConfiguredResult, notConfigured.Result);
        AssertEx.False(notConfigured.ShouldChallenge);
        var missing = Authenticate(requireAuthentication: true, expectedToken: "secret", authorizationHeaders: [], apiKeyHeaders: []);
        AssertEx.False(missing.Allowed);
        AssertEx.Equal(ProxyAdminAuthenticationPolicy.MissingResult, missing.Result);
        AssertEx.True(missing.ShouldChallenge);
        var invalid = Authenticate(requireAuthentication: true, expectedToken: "secret", authorizationHeaders: ["Bearer wrong"], apiKeyHeaders: []);
        AssertEx.False(invalid.Allowed);
        AssertEx.Equal(ProxyAdminAuthenticationPolicy.InvalidResult, invalid.Result);
        var validBearer = Authenticate(requireAuthentication: true, expectedToken: "secret", authorizationHeaders: ["bearer  secret  "], apiKeyHeaders: []);
        AssertEx.True(validBearer.Allowed);
        AssertEx.True(validBearer.AuthenticationRequired);
        AssertEx.Equal(ProxyAdminAuthenticationPolicy.ValidResult, validBearer.Result);
        var validBearerAfterNullRawHeader = Authenticate(requireAuthentication: true, expectedToken: "secret", authorizationHeaders: [null, "Bearer secret"], apiKeyHeaders: []);
        AssertEx.True(validBearerAfterNullRawHeader.Allowed);
        var validApiKey = Authenticate(requireAuthentication: true, expectedToken: "secret", authorizationHeaders: [], apiKeyHeaders: [" secret "]);
        AssertEx.True(validApiKey.Allowed);
        var bearerWinsOverApiKey = Authenticate(requireAuthentication: true, expectedToken: "secret", authorizationHeaders: ["Bearer wrong"], apiKeyHeaders: ["secret"]);
        AssertEx.False(bearerWinsOverApiKey.Allowed);
        AssertEx.Equal(ProxyAdminAuthenticationPolicy.InvalidResult, bearerWinsOverApiKey.Result);
    }

    private static ProxyAdminAuthenticationDecision Authenticate(bool requireAuthentication, string? expectedToken, IReadOnlyList<string?> authorizationHeaders, IReadOnlyList<string?> apiKeyHeaders)
    {
        return ProxyAdminAuthenticationPolicy.Authenticate(new ProxyAdminAuthenticationInput(requireAuthentication, expectedToken, ProxyAdminPresentedCredentials.FromRawHeaders(authorizationHeaders, apiKeyHeaders)));
    }
}
