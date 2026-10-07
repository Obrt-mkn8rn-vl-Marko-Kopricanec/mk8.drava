using Mk8.Drava.Application.INF.Acme;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class AcmeDns01PropagationNetworkTests
{
    [Fact]
    public async Task ActualTxtAnswersRequireTheExactDigestAndAreNotCachedAsync()
    {
        var answer = "foreign value";
        var server = new DevelopmentDnsServer(() => (IReadOnlyList<string>)[answer]);
        await using var lifetime = server.ConfigureAwait(false);
        var verifier = new AcmeDns01PropagationVerifier("127.0.0.1", server.Port);
        var value = new string('A', 43);
        Assert.False(await verifier.VerifyAsync("_acme-challenge.site.example", value, CancellationToken.None).ConfigureAwait(true));
        answer = value;
        Assert.True(await verifier.VerifyAsync("_acme-challenge.site.example", value, CancellationToken.None).ConfigureAwait(true));
        answer = "foreign value";
        Assert.False(await verifier.VerifyAsync("_acme-challenge.site.example", value, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ForeignTxtValuesCanCoexistAndARecordsCannotProveDns01Async()
    {
        var value = new string('A', 43);
        var txt = new DevelopmentDnsServer(() => (IReadOnlyList<string>)["unrelated value", value]);
        await using var txtLifetime = txt.ConfigureAwait(false);
        var addresses = new DevelopmentDnsServer(System.Net.IPAddress.Loopback);
        await using var addressLifetime = addresses.ConfigureAwait(false);
        Assert.True(await new AcmeDns01PropagationVerifier("127.0.0.1", txt.Port).VerifyAsync("_acme-challenge.site.example", value, CancellationToken.None).ConfigureAwait(true));
        Assert.False(await new AcmeDns01PropagationVerifier("127.0.0.1", addresses.Port).VerifyAsync("_acme-challenge.site.example", value, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task AlreadyCancelledProofDoesNotWaitForResolverTimeoutAsync()
    {
        var server = new DevelopmentDnsServer(() => (IReadOnlyList<string>)[]);
        await using var lifetime = server.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync().ConfigureAwait(true);
        var verifier = new AcmeDns01PropagationVerifier("127.0.0.1", server.Port);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await verifier.VerifyAsync("_acme-challenge.site.example", new string('A', 43), cancellation.Token).ConfigureAwait(false)).ConfigureAwait(true);
    }

    [Fact]
    public async Task CancellationJoinsAnActualOutstandingUdpReceiveAsync()
    {
        var server = new DevelopmentDnsServer(() => (IReadOnlyList<string>)[]) { DropReplies = true };
        await using var lifetime = server.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        var verifier = new AcmeDns01PropagationVerifier("127.0.0.1", server.Port);
        var query = Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await verifier.VerifyAsync("_acme-challenge.site.example", new string('A', 43), cancellation.Token).ConfigureAwait(false));
        try { await server.QueryReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); }
        finally { await cancellation.CancelAsync().ConfigureAwait(true); await query.ConfigureAwait(true); }
        Assert.True(query.IsCompletedSuccessfully);
    }
}
