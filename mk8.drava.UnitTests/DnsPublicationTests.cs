using System.Text.Json;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Administration;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class DnsPublicationTests
{
    [Fact]
    public async Task MissingIpv4AndIpv6RecordsAreCreatedWithScopedOwnershipAndConfiguredTtlAsync()
    {
        using var fixture = new PublisherFixture(["192.0.2.10", "2001:db8::10"]);
        Assert.True(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
        var writes = fixture.Handler.Requests.Where(static request => request.Method == HttpMethod.Post).ToArray();
        Assert.Equal(2, writes.Length);
        foreach (var write in writes)
        {
            using var json = JsonDocument.Parse(write.Body);
            Assert.Equal("svc.site.example", json.RootElement.GetProperty("name").GetString());
            Assert.Equal("mk8.drava site=development", json.RootElement.GetProperty("comment").GetString());
            Assert.Equal(300, json.RootElement.GetProperty("ttl").GetInt32());
            Assert.False(json.RootElement.GetProperty("proxied").GetBoolean());
        }
        var count = fixture.Handler.Requests.Length;
        Assert.True(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(count, fixture.Handler.Requests.Length);
        Assert.All(fixture.Handler.Requests, static request => Assert.True(request.Method == HttpMethod.Get || request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task MatchingForeignRecordsAreUsedWithoutBeingAdoptedOrModifiedAsync()
    {
        using var fixture = new PublisherFixture();
        fixture.Handler.Records.Add(new ProviderRecord("svc.site.example", "A", "192.0.2.10"));
        Assert.True(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
        Assert.DoesNotContain(fixture.Handler.Requests, static request => request.Method != HttpMethod.Get);
        Assert.Collection(fixture.Handler.Records, static record => Assert.Equal("foreign owner", record.Comment));
    }

    [Theory]
    [InlineData("wrong-address")]
    [InlineData("proxied")]
    [InlineData("cname")]
    [InlineData("ns")]
    [InlineData("delegation")]
    [InlineData("wrong-name")]
    [InlineData("duplicate-record")]
    public async Task ConflictingForeignRecordsAreNeverOverwrittenAsync(string conflict)
    {
        using var fixture = new PublisherFixture();
        var record = conflict switch
        {
            "wrong-address" => new ProviderRecord("svc.site.example", "A", "192.0.2.20"),
            "proxied" => new ProviderRecord("svc.site.example", "A", "192.0.2.10", Proxied: true),
            "cname" => new ProviderRecord("svc.site.example", "CNAME", "foreign.example"),
            "ns" => new ProviderRecord("svc.site.example", "NS", "ns.foreign.example"),
            "delegation" => new ProviderRecord("tenant.site.example", "NS", "ns.foreign.example"),
            "wrong-name" => new ProviderRecord("svc.site.example", "AAAA", "192.0.2.10"),
            _ => new ProviderRecord("svc.site.example", "A", "192.0.2.10"),
        };
        fixture.Handler.Records.Add(record);
        if (string.Equals(conflict, "duplicate-record", StringComparison.Ordinal)) fixture.Handler.Records.Add(record);
        var host = string.Equals(conflict, "delegation", StringComparison.Ordinal) ? "svc.tenant.site.example" : "svc.site.example";
        Assert.False(await fixture.Publisher.EnsureAsync(host, CancellationToken.None).ConfigureAwait(true));
        Assert.DoesNotContain(fixture.Handler.Requests, static request => request.Method != HttpMethod.Get);
        Assert.Contains(record, fixture.Handler.Records);
    }

    [Theory]
    [InlineData("zone")]
    [InlineData("rate")]
    [InlineData("redirect")]
    [InlineData("duplicate")]
    [InlineData("oversize")]
    [InlineData("oversize-stream")]
    [InlineData("malformed")]
    [InlineData("pagination")]
    [InlineData("rejected")]
    public async Task UnconfirmedOrUnboundedProviderResponsesCannotPublishAsync(string failure)
    {
        using var fixture = new PublisherFixture(); fixture.Handler.Failure = failure;
        Assert.False(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
        Assert.DoesNotContain(fixture.Handler.Requests, static request => request.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("svc.foreign.example")]
    [InlineData("svc.site.example.foreign.example")]
    [InlineData("svcsite.example")]
    [InlineData("SVC.site.example")]
    [InlineData("*.site.example")]
    [InlineData("svc..site.example")]
    [InlineData("svc.site.example/")]
    public async Task InvalidOrOutOfSiteHostsNeverReachTheProviderAsync(string host)
    {
        using var fixture = new PublisherFixture();
        Assert.False(await fixture.Publisher.EnsureAsync(host, CancellationToken.None).ConfigureAwait(true));
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task RetryTimingIsMonotonicAndDoesNotRepeatOnWallClockRollbackAsync()
    {
        using var fixture = new PublisherFixture(); fixture.Handler.Failure = "rate";
        Assert.False(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
        var count = fixture.Handler.Requests.Length;
        fixture.Clock.AdjustUtc(TimeSpan.FromDays(-1)); fixture.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(count, fixture.Handler.Requests.Length);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1)); fixture.Handler.Failure = "";
        Assert.True(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, fixture.Handler.Requests.Count(static request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task CancellationPropagatesAndReleasesTheOwnedAttemptAsync()
    {
        using var fixture = new PublisherFixture(); fixture.Handler.Block = true;
        using var cancellation = new CancellationTokenSource();
        var attempt = fixture.Publisher.EnsureAsync("svc.site.example", cancellation.Token).AsTask();
        await fixture.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await cancellation.CancelAsync().ConfigureAwait(true);
        try { await attempt.ConfigureAwait(true); Assert.Fail("The owned request did not propagate cancellation."); }
        catch (OperationCanceledException) { Assert.True(cancellation.IsCancellationRequested); }
        fixture.Handler.Block = false;
        Assert.True(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task FourActiveOperationsBoundAdmissionWithoutAnUnboundedWaiterQueueAsync()
    {
        using var fixture = new PublisherFixture(); fixture.Handler.Block = true;
        using var cancellation = new CancellationTokenSource();
        var attempts = Enumerable.Range(0, 4).Select(index => fixture.Publisher.EnsureAsync("svc" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".site.example", cancellation.Token).AsTask()).ToArray();
        try
        {
            await fixture.Handler.FourEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            Assert.False(await fixture.Publisher.EnsureAsync("overflow.site.example", CancellationToken.None).ConfigureAwait(true));
            Assert.False(await fixture.Publisher.EnsureAsync("svc0.site.example", CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(4, fixture.Handler.Requests.Length);
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(true);
            try { await Task.WhenAll(attempts).ConfigureAwait(true); }
            catch (OperationCanceledException) { Assert.True(cancellation.IsCancellationRequested); }
        }
        fixture.Handler.Block = false;
        Assert.True(await fixture.Publisher.EnsureAsync("overflow.site.example", CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ConcurrentInstancesForOneHostnameHaveOneActiveWriterAsync()
    {
        using var fixture = new PublisherFixture(); fixture.Handler.Block = true;
        using var cancellation = new CancellationTokenSource();
        var attempt = fixture.Publisher.EnsureAsync("svc.site.example", cancellation.Token).AsTask();
        try
        {
            await fixture.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            Assert.False(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
            Assert.Collection(fixture.Handler.Requests, static request => Assert.Equal(HttpMethod.Get, request.Method));
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(true);
            try { await attempt.ConfigureAwait(true); Assert.Fail("The owned writer did not propagate cancellation."); }
            catch (OperationCanceledException) { Assert.True(cancellation.IsCancellationRequested); }
        }
        fixture.Handler.Block = false;
        Assert.True(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ProviderDeadlineClosesTheAttemptAndPermitsALaterRetryAsync()
    {
        using var fixture = new PublisherFixture(timeoutSeconds: 1); fixture.Handler.Block = true;
        Assert.False(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
        fixture.Handler.Block = false; fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ProviderAcknowledgmentNeverReplacesActualDnsVerificationAsync()
    {
        var verifier = new MissingDnsVerifier(); var publisher = new ConfirmedPublisher();
        var composed = new PublishingServiceDnsVerifier(verifier, publisher);
        Assert.False((await composed.VerifyAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true)).Verified);
        Assert.Equal(2, verifier.Calls); Assert.Equal(1, publisher.Calls);
    }

    [Fact]
    public async Task AnIncorrectCreateAcknowledgmentIsNotTrustedAsync()
    {
        using var fixture = new PublisherFixture(); fixture.Handler.Failure = "created-host";
        Assert.False(await fixture.Publisher.EnsureAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true));
        Assert.Collection(fixture.Handler.Requests.Where(static request => request.Method == HttpMethod.Post), static request => Assert.Equal(HttpMethod.Post, request.Method));
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("foreign-zone")]
    [InlineData("zone-id")]
    [InlineData("relative-credential")]
    [InlineData("ttl")]
    [InlineData("deadline")]
    [InlineData("retry")]
    public void InvalidPublisherBootstrapIsRejectedBeforeProviderWork(string invalid)
    {
        var settings = PublisherFixture.Settings("/private/token") with { };
        settings = invalid switch
        {
            "unsupported" => settings with { Provider = "arbitrary" },
            "foreign-zone" => settings with { ZoneName = "foreign.example" },
            "zone-id" => settings with { ZoneId = "../foreign" },
            "relative-credential" => settings with { CredentialPath = "token" },
            "ttl" => settings with { TtlSeconds = 59 },
            "deadline" => settings with { RequestTimeoutSeconds = 31 },
            _ => settings with { RetrySeconds = 4 },
        };
        Assert.Throws<InvalidDataException>(() => settings.Validate("site.example"));
    }

    [Fact]
    public void ExistingDnsRemainsTheDefaultAndTheSharedReaderRetainsTheAdministratorContract()
    {
        var settings = new ControllerBootstrap(); Assert.Equal("existing", settings.DnsPublication.Provider);
        settings.DnsPublication.Validate("site.example");
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "token");
        File.WriteAllText(path, new string('A', 40));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(AdministratorCredentialFile.Read(path), PrivateBearerCredentialFile.Read(path));
        File.WriteAllText(path, new string('A', 40) + "\n");
        Assert.Throws<InvalidDataException>(() => PrivateBearerCredentialFile.Read(path));
    }

    private sealed class PublisherFixture : IDisposable
    {
        private readonly RegistryStateDirectory _directory = new();
        public RegistryTimeProvider Clock { get; } = new();
        public DevelopmentDnsProvider Handler { get; } = new();
        public CloudflareDnsPublisher Publisher { get; }
        public PublisherFixture(IReadOnlyList<string>? addresses = null, int timeoutSeconds = 5)
        {
            var path = Path.Combine(_directory.Path, "provider.token"); File.WriteAllText(path, new string('A', 40));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Publisher = new CloudflareDnsPublisher(Settings(path) with { RequestTimeoutSeconds = timeoutSeconds }, "site.example", "development", addresses ?? ["192.0.2.10"], Clock, Handler);
        }
        public static DnsPublicationSettings Settings(string path) => new() { Provider = "cloudflare", ZoneId = new string('a', 32), ZoneName = "site.example", CredentialPath = path };
        public void Dispose() { Publisher.Dispose(); _directory.Dispose(); }
    }

    private sealed class MissingDnsVerifier : IServiceDnsVerifier
    {
        public int Calls { get; private set; }
        public ValueTask<DnsPublicationProof> VerifyAsync(string host, CancellationToken cancellationToken) { _ = host; _ = cancellationToken; Calls++; return ValueTask.FromResult(DnsPublicationProof.Missing); }
    }
    private sealed class ConfirmedPublisher : IServiceDnsPublisher
    {
        public int Calls { get; private set; }
        public ValueTask<bool> EnsureAsync(string host, CancellationToken cancellationToken) { _ = host; _ = cancellationToken; Calls++; return ValueTask.FromResult(true); }
    }
}
