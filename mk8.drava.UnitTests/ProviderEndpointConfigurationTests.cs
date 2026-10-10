using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProviderEndpointConfigurationTests
{
    [Theory]
    [InlineData("absent")]
    [InlineData("relative")]
    [InlineData("http")]
    [InlineData("file")]
    [InlineData("userinfo")]
    [InlineData("query")]
    [InlineData("fragment")]
    [InlineData("unterminated-base")]
    public void ProviderEndpointsRequireExplicitHttpsAuthority(string fault)
    {
        var endpoint = fault switch
        {
            "absent" => null,
            "relative" => new Uri("client/v4/", UriKind.Relative),
            "http" => new Uri("http://api.dns.invalid/client/v4/"),
            "file" => new Uri("file:///client/v4/"),
            "userinfo" => new Uri("https://owner@api.dns.invalid/client/v4/"),
            "query" => new Uri("https://api.dns.invalid/client/v4/?token=value"),
            "fragment" => new Uri("https://api.dns.invalid/client/v4/#fragment"),
            "unterminated-base" => new Uri("https://api.dns.invalid/client/v4"),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        var settings = Settings() with { ApiBaseUrl = endpoint };
        Assert.Throws<InvalidDataException>(() => settings.Validate("site.test"));
    }

    [Fact]
    public void VerificationOnlyDnsDoesNotAcceptAnUnusedProviderEndpoint()
    {
        var settings = new DnsPublicationSettings { ApiBaseUrl = new Uri("https://api.dns.invalid/client/v4/") };
        Assert.Throws<InvalidDataException>(() => settings.Validate("site.test"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitProviderAndIssuanceEnvironmentSurviveStrictConfigurationRoundTripAsync(bool useStaging)
    {
        using var directory = new RegistryStateDirectory();
        var dnsPath = Path.Combine(directory.Path, "dns.json");
        var acmePath = Path.Combine(directory.Path, "acme.json");
        var dns = Settings();
        var acme = new AcmeIssuanceSettings { DirectoryUrl = new Uri("https://ca.invalid/directory"), UseStaging = useStaging };
        await File.WriteAllTextAsync(dnsPath, BootstrapFile.Serialize(dns)).ConfigureAwait(true);
        await File.WriteAllTextAsync(acmePath, BootstrapFile.Serialize(acme)).ConfigureAwait(true);
        var loadedDns = await BootstrapFile.LoadAsync<DnsPublicationSettings>(dnsPath, CancellationToken.None).ConfigureAwait(true);
        var loadedAcme = await BootstrapFile.LoadAsync<AcmeIssuanceSettings>(acmePath, CancellationToken.None).ConfigureAwait(true);
        loadedDns.Validate("site.test");
        Assert.Equal(dns.ApiBaseUrl, loadedDns.RequireApiBaseUrl());
        Assert.Equal(acme.DirectoryUrl, loadedAcme.RequireDirectoryUrl());
        Assert.Equal(useStaging, loadedAcme.UseStaging);
    }

    private static DnsPublicationSettings Settings() => new()
    {
        Provider = "cloudflare", ApiBaseUrl = new Uri("https://api.dns.invalid/client/v4/"),
        ZoneId = new string('a', 32), ZoneName = "site.test", CredentialPath = Path.GetFullPath("fixture-credential"),
    };
}
