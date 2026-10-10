using Mk8.Drava.Application.INF.NoConf;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class CloudflareDns01RecordTests
{
    [Fact]
    public async Task ExistingProviderAdapterVerifiesCreatedDns01TxtContentAsync()
    {
        using var provider = new DevelopmentDnsProvider();
        using var api = new CloudflareDnsApi(new Uri("https://api.dns.invalid/client/v4/"), new string('a', 32), new string('A', 40), provider);
        var host = "_acme-challenge.site.example";
        var value = new string('A', 43);
        var confirmed = await api.CreateAsync(host, "TXT", value, 60, "mk8.drava acme site=site order=owned", CancellationToken.None).ConfigureAwait(true);
        Assert.True(confirmed);
        Assert.Collection(provider.Records, record =>
        {
            Assert.Equal(host, record.Name);
            Assert.Equal("TXT", record.Type);
            Assert.Equal("\"" + value + "\"", record.Content);
        });
    }

    [Fact]
    public async Task CleanupRemovesOnlyTheCreatedRecordAndPreservesOtherTxtValuesAsync()
    {
        using var provider = new DevelopmentDnsProvider();
        var foreign = new ProviderRecord("_acme-challenge.site.example", "TXT", "\"foreign value\"");
        provider.Records.Add(foreign);
        using var api = new CloudflareDnsApi(new Uri("https://api.dns.invalid/client/v4/"), new string('a', 32), new string('A', 40), provider);
        var identity = await api.CreateTxtAsync(foreign.Name, new string('A', 43), 60, "mk8.drava acme site=site order=owned", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, provider.Records.Count);
        Assert.True(await api.DeleteTxtAsync(identity, CancellationToken.None).ConfigureAwait(true));
        Assert.Collection(provider.Records, record => Assert.Equal(foreign, record));
        Assert.Collection(provider.Requests, static request => Assert.Equal(HttpMethod.Post, request.Method),
            static request => Assert.Equal(HttpMethod.Get, request.Method), static request => Assert.Equal(HttpMethod.Delete, request.Method));
        Assert.EndsWith("/" + identity.Id, provider.Requests[2].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("read-owner")]
    [InlineData("read-host")]
    [InlineData("read-value")]
    [InlineData("read-type")]
    [InlineData("read-id")]
    [InlineData("read-proxied")]
    public async Task ChangedRecordIdentityPreventsCleanupAsync(string corruption)
    {
        using var provider = new DevelopmentDnsProvider();
        using var api = new CloudflareDnsApi(new Uri("https://api.dns.invalid/client/v4/"), new string('a', 32), new string('A', 40), provider);
        var identity = await api.CreateTxtAsync("_acme-challenge.site.example", new string('A', 43), 60, "mk8.drava acme site=site order=owned", CancellationToken.None).ConfigureAwait(true);
        provider.Failure = corruption;
        Assert.False(await api.DeleteTxtAsync(identity, CancellationToken.None).ConfigureAwait(true));
        Assert.Collection(provider.Records, record => Assert.Equal(identity.Id, record.Id));
        Assert.DoesNotContain(provider.Requests, static request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task AnotherZoneCannotConsumeTheCreatedRecordIdentityAsync()
    {
        using var creator = new DevelopmentDnsProvider();
        using var owner = new CloudflareDnsApi(new Uri("https://api.dns.invalid/client/v4/"), new string('a', 32), new string('A', 40), creator);
        var identity = await owner.CreateTxtAsync("_acme-challenge.site.example", new string('A', 43), 60, "mk8.drava acme site=site order=owned", CancellationToken.None).ConfigureAwait(true);
        using var other = new DevelopmentDnsProvider();
        using var foreign = new CloudflareDnsApi(new Uri("https://api.dns.invalid/client/v4/"), new string('c', 32), new string('A', 40), other);
        await Assert.ThrowsAsync<InvalidDataException>(() => foreign.DeleteTxtAsync(identity, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Empty(other.Requests);
        Assert.Collection(creator.Records, record => Assert.Equal(identity.Id, record.Id));
    }
}
