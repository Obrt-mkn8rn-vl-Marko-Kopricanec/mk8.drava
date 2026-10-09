using System.Net;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class Http2HeaderIntegrityProcessTests
{
    [Theory]
    [InlineData("uppercase", false)]
    [InlineData("uppercase", true)]
    [InlineData("field-count", false)]
    [InlineData("field-count", true)]
    [InlineData("table-size", false)]
    [InlineData("table-size", true)]
    [InlineData("integer-overflow", false)]
    [InlineData("integer-overflow", true)]
    [InlineData("whitespace", false)]
    [InlineData("whitespace", true)]
    [InlineData("name-octet", false)]
    [InlineData("name-octet", true)]
    public async Task MalformedUpstreamFieldsReturn502AndNeverEnterTheCacheAsync(string kind, bool publicHttp2)
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        using var upstream = new DevelopmentHttp2Peer(certificate);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: true, manualCache: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        if (!publicHttp2) client.Client.DefaultRequestVersion = HttpVersion.Version11;
        for (var attempt = 0; attempt < 2; attempt++)
            await VerifyAttemptAsync(upstream, client, kind, publicHttp2).ConfigureAwait(true);
    }

    private static async Task VerifyAttemptAsync(DevelopmentHttp2Peer upstream, DevelopmentSiteClient client, string kind, bool publicHttp2)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var peer = upstream.RespondHeaderBlockAsync(HeaderBlock(kind), deadline.Token);
        try
        {
            using var response = await client.Client.GetAsync(new Uri("/header-integrity", UriKind.Relative), deadline.Token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal(publicHttp2 ? HttpVersion.Version20 : HttpVersion.Version11, response.Version);
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
        }
    }

    private static byte[] HeaderBlock(string kind)
    {
        if (string.Equals(kind, "field-count", StringComparison.Ordinal))
        {
            var block = new byte[129]; block[0] = 0x88; block.AsSpan(1).Fill(0x90); return block;
        }
        return Convert.FromHexString(kind switch
        {
            "uppercase" => "880001580179",
            "table-size" => "2188",
            "integer-overflow" => "ff89ffffff0f",
            "whitespace" => "88000178022079",
            "name-octet" => "880001ff0179",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
    }
}
