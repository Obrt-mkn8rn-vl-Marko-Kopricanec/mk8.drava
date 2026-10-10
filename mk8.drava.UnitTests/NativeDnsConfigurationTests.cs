using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class NativeDnsConfigurationTests
{
    private static NativeDnsManagementSettings Profile(bool acme = false) => new()
    {
        Origin = "site.test", TenantId = Guid.NewGuid(), ZoneId = Guid.NewGuid(),
        SocketPath = Path.Combine(Path.GetTempPath(), "dns-management.sock"), CredentialPath = Path.Combine(Path.GetTempPath(), "records-capability"),
        Scopes = [new NativeDnsOwnerScope(acme ? "_acme-challenge.site.test" : "svc.site.test", acme ? (ushort)16 : (ushort)1)],
    };

    [Fact]
    public void NativeProfilesRequireExactOwnerTypesAndConcreteProvisionedIdentities()
    {
        var profile = Profile(); profile.Validate("site.test", false);
        Assert.Throws<InvalidDataException>(() => (profile with { TenantId = Guid.Empty }).Validate("site.test", false));
        Assert.Throws<InvalidDataException>(() => (profile with { Scopes = [new NativeDnsOwnerScope("*.site.test", 1)] }).Validate("site.test", false));
        Assert.Throws<InvalidDataException>(() => (profile with { Scopes = [new NativeDnsOwnerScope("svc.foreign.test", 1)] }).Validate("site.test", false));
        Assert.Throws<InvalidDataException>(() => (profile with { Scopes = [new NativeDnsOwnerScope("svc.site.test", 5)] }).Validate("site.test", false));
        Assert.Throws<InvalidDataException>(() => (profile with { Scopes = [profile.Scopes[0], profile.Scopes[0]] }).Validate("site.test", false));
        Assert.Throws<InvalidDataException>(() => profile.Validate("site.test", true));
    }

    [Fact]
    public void ExistingAndCloudflareSettingsCannotSilentlyAdoptNativeManagement()
    {
        var profile = Profile();
        var settings = new DnsPublicationSettings { Provider = "mk8.dns", NativeManagement = profile, RequestTimeoutSeconds = 20 };
        settings.Validate("site.test");
        Assert.Throws<InvalidDataException>(() => (settings with { NativeManagement = null }).Validate("site.test"));
        Assert.Throws<InvalidDataException>(() => (settings with { RequestTimeoutSeconds = 5 }).Validate("site.test"));
        Assert.Throws<InvalidDataException>(() => (settings with { Provider = "existing" }).Validate("site.test"));
        Assert.Throws<InvalidDataException>(() => (settings with { Provider = "cloudflare" }).Validate("site.test"));
        Assert.Throws<InvalidDataException>(() => (settings with { ZoneName = "site.test" }).Validate("site.test"));
        Assert.Throws<InvalidDataException>(() => (settings with { ApiBaseUrl = new Uri("https://api.dns.invalid/") }).Validate("site.test"));
    }

    [Fact]
    public void EnabledNativeDns01RequiresMatchedZoneAndDistinctCapabilityCustody()
    {
        var records = Profile();
        var settings = new DnsPublicationSettings { Provider = "mk8.dns", NativeManagement = records, RequestTimeoutSeconds = 20 };
        var acme = records with { CredentialPath = Path.Combine(Path.GetTempPath(), "acme-capability"), Scopes = [new NativeDnsOwnerScope("_acme-challenge.site.test", 16)] };
        var issuance = new AcmeIssuanceSettings { Enabled = true, DirectoryUrl = new Uri("https://ca.invalid/directory"), AccountKeyPath = Path.Combine(Path.GetTempPath(), "account.key"), TermsAccepted = true,
            NativeManagement = acme, ContactEmails = ["owner@site.test"], CleanupTimeoutSeconds = 20 };
        var trust = new ServingTrustSettings { Mode = "system" };
        issuance.Validate(trust, settings);
        Assert.Throws<InvalidDataException>(() => (issuance with { NativeManagement = acme with { CredentialPath = records.CredentialPath } }).Validate(trust, settings));
        Assert.Throws<InvalidDataException>(() => (issuance with { NativeManagement = acme with { ZoneId = Guid.NewGuid() } }).Validate(trust, settings));
        Assert.Throws<InvalidDataException>(() => (issuance with { CleanupJournalPath = Path.Combine(Path.GetTempPath(), "cloudflare-cleanup") }).Validate(trust, settings));
        Assert.Throws<InvalidDataException>(() => (issuance with { CleanupTimeoutSeconds = 15 }).Validate(trust, settings));
    }
}
