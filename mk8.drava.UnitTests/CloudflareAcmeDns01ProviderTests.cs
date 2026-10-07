using Mk8.Drava.Application.INF.Acme;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class CloudflareAcmeDns01ProviderTests
{
    [Fact]
    public async Task MultipleChallengeValuesCoexistWithForeignTxtAndCleanupUsesEachExactIdentityAsync()
    {
        using var fixture = new ProviderFixture();
        var foreign = new ProviderRecord("_acme-challenge.site.example", "TXT", "\"foreign\"");
        fixture.Handler.Records.Add(foreign);
        var first = await fixture.Provider.PublishAsync(foreign.Name, new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(true);
        var second = await fixture.Provider.PublishAsync(foreign.Name, new string('B', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(3, fixture.Handler.Records.Count);
        Assert.True(await fixture.Provider.IsPropagatedAsync(first, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal((first.Host, first.Value), fixture.Verifier.Last);
        await fixture.Provider.RemoveAsync(first, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, fixture.Handler.Records.Count); Assert.Contains(foreign, fixture.Handler.Records);
        Assert.Contains(fixture.Handler.Records, r => string.Equals(r.Content, "\"" + second.Value + "\"", StringComparison.Ordinal));
        await fixture.Provider.RemoveAsync(second, CancellationToken.None).ConfigureAwait(true);
        Assert.Collection(fixture.Handler.Records, r => Assert.Equal(foreign, r));
    }

    [Theory]
    [InlineData("foreign.example")]
    [InlineData("site.example.evil.example")]
    [InlineData("*.site.example")]
    public async Task ForeignAndNoncanonicalOwnersNeverReachProviderAsync(string domain)
    {
        using var fixture = new ProviderFixture();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Provider.PublishAsync("_acme-challenge." + domain, new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Empty(fixture.Handler.Requests); Assert.Empty(fixture.Handler.Records);
    }

    [Theory]
    [InlineData("zone")]
    [InlineData("delegation")]
    [InlineData("alias")]
    public async Task WrongZoneDelegationAndAliasAreRejectedWithoutCreatingChallengeAsync(string conflict)
    {
        using var fixture = new ProviderFixture();
        if (string.Equals(conflict, "zone", StringComparison.Ordinal)) fixture.Handler.Failure = "zone";
        else fixture.Handler.Records.Add(new ProviderRecord("_acme-challenge.site.example", string.Equals(conflict, "alias", StringComparison.Ordinal) ? "CNAME" : "NS", "foreign.example"));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Provider.PublishAsync("_acme-challenge.site.example", new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.DoesNotContain(fixture.Handler.Requests, static r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task AnotherProviderInstanceCannotDeleteTheRecordAsync()
    {
        using var first = new ProviderFixture(); using var second = new ProviderFixture();
        var record = await first.Provider.PublishAsync("_acme-challenge.site.example", new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await second.Provider.RemoveAsync(record, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Empty(second.Handler.Requests); Assert.Collection(first.Handler.Records, record => Assert.Equal("_acme-challenge.site.example", record.Name));
        await first.Provider.RemoveAsync(record, CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task ChangedOwnershipCannotBeReportedAsCleanedAsync()
    {
        using var fixture = new ProviderFixture();
        var record = await fixture.Provider.PublishAsync("_acme-challenge.site.example", new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(true);
        fixture.Handler.Failure = "read-owner";
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Provider.RemoveAsync(record, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Collection(fixture.Handler.Records, record => Assert.Equal("_acme-challenge.site.example", record.Name)); Assert.DoesNotContain(fixture.Handler.Requests, static r => r.Method == HttpMethod.Delete);
    }

    private sealed class ProviderFixture : IDisposable
    {
        private readonly RegistryStateDirectory _directory = new();
        public DevelopmentDnsProvider Handler { get; } = new();
        public DevelopmentVerifier Verifier { get; } = new();
        public CloudflareAcmeDns01ChallengeProvider Provider { get; }
        public ProviderFixture()
        {
            var path = Path.Combine(_directory.Path, "provider.token"); File.WriteAllText(path, new string('A', 40));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var settings = new Mk8.Drava.Configuration.DnsPublicationSettings { Provider = "cloudflare", ZoneId = new string('a', 32), ZoneName = "site.example", CredentialPath = path };
            Provider = new CloudflareAcmeDns01ChallengeProvider(settings, "site.example", "development", Verifier, Handler);
        }
        public void Dispose() { Provider.Dispose(); Handler.Dispose(); _directory.Dispose(); }
    }
    private sealed class DevelopmentVerifier : IAcmeDns01PropagationVerifier
    {
        public (string Host, string Value) Last { get; private set; }
        public ValueTask<bool> VerifyAsync(string host, string value, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Last = (host, value); return ValueTask.FromResult(true); }
    }
}
