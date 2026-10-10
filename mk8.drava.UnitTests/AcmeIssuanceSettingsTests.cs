using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AcmeIssuanceSettingsTests
{
    [Fact]
    public void DisabledDefaultDoesNotRequireContactsCredentialsOrAcceptedTerms()
    {
        var settings = new AcmeIssuanceSettings();
        Assert.Null(settings.DirectoryUrl);
        settings.Validate(new ServingTrustSettings(), new DnsPublicationSettings());
    }

    [Theory]
    [InlineData("trust")]
    [InlineData("provider")]
    [InlineData("absent-directory")]
    [InlineData("relative-directory")]
    [InlineData("http")]
    [InlineData("userinfo")]
    [InlineData("query")]
    [InlineData("fragment")]
    [InlineData("account")]
    [InlineData("journal")]
    [InlineData("collision")]
    [InlineData("contacts")]
    [InlineData("email")]
    [InlineData("terms")]
    [InlineData("pinned-root")]
    [InlineData("system-root")]
    [InlineData("request")]
    [InlineData("operation")]
    [InlineData("poll")]
    [InlineData("cleanup")]
    [InlineData("check")]
    [InlineData("retry")]
    public void EnabledIssuanceRequiresExplicitOwnerScopeAndBoundedSettings(string fault)
    {
        var settings = Approved(); var trust = new ServingTrustSettings { Mode = "system" }; var dns = new DnsPublicationSettings { Provider = "cloudflare" };
        settings = fault switch
        {
            "absent-directory" => settings with { DirectoryUrl = null },
            "relative-directory" => settings with { DirectoryUrl = new Uri("directory", UriKind.Relative) },
            "http" => settings with { DirectoryUrl = new Uri("http://ca.example/directory") },
            "userinfo" => settings with { DirectoryUrl = new Uri("https://secret@ca.example/directory") },
            "query" => settings with { DirectoryUrl = new Uri("https://ca.example/directory?secret=value") },
            "fragment" => settings with { DirectoryUrl = new Uri("https://ca.example/directory#fragment") },
            "account" => settings with { AccountKeyPath = "relative.pem" },
            "journal" => settings with { CleanupJournalPath = "relative.json" },
            "collision" => settings with { CleanupJournalPath = settings.AccountKeyPath },
            "contacts" => settings with { ContactEmails = [] },
            "email" => settings with { ContactEmails = ["ops@ example.org"] },
            "terms" => settings with { TermsAccepted = false },
            "system-root" => settings with { PinnedServingRootPath = Path.GetFullPath("public-root.der") },
            "request" => settings with { RequestTimeoutSeconds = 31 },
            "operation" => settings with { OperationTimeoutSeconds = 601 },
            "poll" => settings with { PollIntervalSeconds = 0 },
            "cleanup" => settings with { CleanupTimeoutSeconds = 31 },
            "check" => settings with { CheckIntervalSeconds = 9 },
            "retry" => settings with { RetryAfterSeconds = 29 },
            _ => settings,
        };
        if (string.Equals(fault, "trust", StringComparison.Ordinal)) trust = new ServingTrustSettings();
        if (string.Equals(fault, "pinned-root", StringComparison.Ordinal)) trust = new ServingTrustSettings { Mode = "pinned", RootFingerprint = new string('A', 64) };
        if (string.Equals(fault, "provider", StringComparison.Ordinal)) dns = new DnsPublicationSettings();
        Assert.Throws<InvalidDataException>(() => settings.Validate(trust, dns));
    }

    [Fact]
    public void SystemAndPinnedModesKeepTheApprovedPublicAuthoritySeparate()
    {
        var settings = Approved(); var dns = new DnsPublicationSettings { Provider = "cloudflare" };
        settings.Validate(new ServingTrustSettings { Mode = "system" }, dns);
        (settings with { PinnedServingRootPath = Path.GetFullPath("public-root.der") }).Validate(new ServingTrustSettings { Mode = "pinned", RootFingerprint = new string('A', 64) }, dns);
    }

    private static AcmeIssuanceSettings Approved() => new()
    {
        Enabled = true, TermsAccepted = true, DirectoryUrl = new Uri("https://ca.example/directory"), ContactEmails = ["ops@example.org"],
        AccountKeyPath = Path.GetFullPath("account.pem"), CleanupJournalPath = Path.GetFullPath("cleanup.json"),
    };
}
