using System.Net;
using Mk8.Drava.Application.INF.NoConf;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class ServiceDnsOwnershipTests
{
    [Fact]
    public async Task ActualUdpIpv4AddressProofIsBoundedByThePublicationLeaseAsync()
    {
        var server = new DevelopmentDnsServer(IPAddress.Loopback); await using var serverLifetime = server.ConfigureAwait(true);
        var verifier = new ServiceDnsVerifier(["127.0.0.1"], "127.0.0.1", server.Port);
        var proof = await verifier.VerifyAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true);
        Assert.True(proof.Verified); Assert.Equal(TimeSpan.FromSeconds(30), proof.Validity); Assert.True(server.QueryReceived.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ActualUdpIpv6AddressProofUsesTheAaaaRecordWithoutRequiringAnIpv4AnswerAsync()
    {
        var server = new DevelopmentDnsServer(IPAddress.Parse("2001:db8::10")); await using var serverLifetime = server.ConfigureAwait(true);
        var verifier = new ServiceDnsVerifier(["2001:db8::10"], "127.0.0.1", server.Port);
        Assert.True((await verifier.VerifyAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true)).Verified);
    }

    [Fact]
    public async Task UnapprovedReturnedAddressCannotCreatePublicationProofAsync()
    {
        var server = new DevelopmentDnsServer(IPAddress.Parse("192.0.2.20")); await using var serverLifetime = server.ConfigureAwait(true);
        var verifier = new ServiceDnsVerifier(["192.0.2.10"], "127.0.0.1", server.Port);
        Assert.False((await verifier.VerifyAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true)).Verified);
    }

    [Fact]
    public async Task OwnerCancellationCompletesTheAwaitedActualUdpReceiveAsync()
    {
        var server = new DevelopmentDnsServer(IPAddress.Loopback) { DropReplies = true }; await using var serverLifetime = server.ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource(); var verifier = new ServiceDnsVerifier(["127.0.0.1"], "127.0.0.1", server.Port);
        var query = Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await verifier.VerifyAsync("svc.site.example", cancellation.Token).ConfigureAwait(false));
        try { await server.QueryReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); }
        finally { await cancellation.CancelAsync().ConfigureAwait(true); await query.ConfigureAwait(true); }
        Assert.True(query.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task OwnedTotalDeadlineReturnsMissingWhenTheResolverDropsDatagramsAsync()
    {
        var server = new DevelopmentDnsServer(IPAddress.Loopback) { DropReplies = true }; await using var serverLifetime = server.ConfigureAwait(true);
        var verifier = new ServiceDnsVerifier(["127.0.0.1"], "127.0.0.1", server.Port);
        Assert.False((await verifier.VerifyAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true)).Verified);
        Assert.True(server.QueryReceived.Task.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncatedUdpUsesOwnedTcpForBothQuestionsAndInvalidFramesCannotProvePublicationAsync(bool invalid)
    {
        var port = DevelopmentPortAllocator.GetPort();
        var udp = new DevelopmentDnsServer(IPAddress.Loopback, port) { TruncateReplies = true }; await using var udpLifetime = udp.ConfigureAwait(true);
        var tcp = new DevelopmentDnsTcpServer(port, IPAddress.Loopback) { InvalidFrame = invalid }; await using var tcpLifetime = tcp.ConfigureAwait(true);
        var verifier = new ServiceDnsVerifier(["127.0.0.1"], "127.0.0.1", port);
        Assert.Equal(!invalid, (await verifier.VerifyAsync("svc.site.example", CancellationToken.None).ConfigureAwait(true)).Verified);
        Assert.True(tcp.QueryReceived.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task OwnerCancellationCompletesTheAwaitedTcpResponseReadAfterUdpTruncationAsync()
    {
        var port = DevelopmentPortAllocator.GetPort();
        var udp = new DevelopmentDnsServer(IPAddress.Loopback, port) { TruncateReplies = true }; await using var udpLifetime = udp.ConfigureAwait(true);
        var tcp = new DevelopmentDnsTcpServer(port, IPAddress.Loopback) { BlockResponse = true }; await using var tcpLifetime = tcp.ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource(); var verifier = new ServiceDnsVerifier(["127.0.0.1"], "127.0.0.1", port);
        var query = Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await verifier.VerifyAsync("svc.site.example", cancellation.Token).ConfigureAwait(false));
        try { await tcp.QueryReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); }
        finally { await cancellation.CancelAsync().ConfigureAwait(true); await query.ConfigureAwait(true); }
        Assert.True(query.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PreCanceledOwnerDoesNotOpenADnsQueryAsync()
    {
        var server = new DevelopmentDnsServer(IPAddress.Loopback); await using var serverLifetime = server.ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync().ConfigureAwait(true);
        var verifier = new ServiceDnsVerifier(["127.0.0.1"], "127.0.0.1", server.Port);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verifier.VerifyAsync("svc.site.example", cancellation.Token).AsTask()).ConfigureAwait(true);
        Assert.False(server.QueryReceived.Task.IsCompleted);
    }
}
