using Mk8.Drava.Application.INF.Acme;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class AcmeDns01TcpNetworkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncatedUdpResponseUsesBoundedTcpAndInvalidFramesCannotProvePublicationAsync(bool invalid)
    {
        var port = DevelopmentPortAllocator.GetPort(); var value = new string('A', 43);
        var udp = new DevelopmentDnsServer(() => (IReadOnlyList<string>)[], port) { TruncateReplies = true };
        await using var udpLifetime = udp.ConfigureAwait(false);
        var tcp = new DevelopmentDnsTcpServer(port, [value]) { InvalidFrame = invalid };
        await using var tcpLifetime = tcp.ConfigureAwait(false);
        var verifier = new AcmeDns01PropagationVerifier("127.0.0.1", port);
        Assert.Equal(!invalid, await verifier.VerifyAsync("_acme-challenge.site.example", value, CancellationToken.None).ConfigureAwait(true));
        Assert.True(tcp.QueryReceived.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task OwnerCancellationJoinsActualTcpResponseReadAfterUdpTruncationAsync()
    {
        var port = DevelopmentPortAllocator.GetPort();
        var udp = new DevelopmentDnsServer(() => (IReadOnlyList<string>)[], port) { TruncateReplies = true };
        await using var udpLifetime = udp.ConfigureAwait(false);
        var tcp = new DevelopmentDnsTcpServer(port, []) { BlockResponse = true };
        await using var tcpLifetime = tcp.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        var verifier = new AcmeDns01PropagationVerifier("127.0.0.1", port);
        var query = Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await verifier.VerifyAsync("_acme-challenge.site.example", new string('A', 43), cancellation.Token).ConfigureAwait(false));
        try { await tcp.QueryReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); }
        finally { await cancellation.CancelAsync().ConfigureAwait(true); await query.ConfigureAwait(true); }
        Assert.True(query.IsCompletedSuccessfully);
    }
}
