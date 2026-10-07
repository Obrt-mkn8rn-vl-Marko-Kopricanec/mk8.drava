using System.Net;
using Mk8.Drava.Application.INF.Acme;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AcmeOperationHttpClientTests
{
    [Theory]
    [InlineData("https://foreign.example/order")]
    [InlineData("http://ca.example/order")]
    [InlineData("https://owner@ca.example/order")]
    [InlineData("https://ca.example/order#fragment")]
    public async Task UnapprovedEndpointsNeverReachTheHandlerAsync(string endpoint)
    {
        using var handler = new DevelopmentAcmeHttpHandler();
        using var operation = new AcmeOperationHttpClient(new Uri("https://ca.example/directory"), TimeSpan.FromSeconds(5), CancellationToken.None, handler);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            using var response = await operation.Client.GetAsync(new Uri(endpoint)).ConfigureAwait(false);
        }).ConfigureAwait(true);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task RedirectResponsesCannotChangeTheApprovedOriginAsync()
    {
        using var handler = new DevelopmentAcmeHttpHandler { Status = HttpStatusCode.Redirect };
        using var operation = new AcmeOperationHttpClient(new Uri("https://ca.example/directory"), TimeSpan.FromSeconds(5), CancellationToken.None, handler);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            using var response = await operation.Client.GetAsync(new Uri("https://ca.example/order")).ConfigureAwait(false);
        }).ConfigureAwait(true);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task CancelJoinsTheRealRequestAndRejectsSubsequentRequestsAsync()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new DevelopmentAcmeHttpHandler { BlockHeaders = true };
        using var operation = new AcmeOperationHttpClient(new Uri("https://ca.example/directory"), TimeSpan.FromSeconds(5), cancellation.Token, handler);
        var request = Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            using var response = await operation.Client.GetAsync(new Uri("https://ca.example/order")).ConfigureAwait(false);
        });
        try { await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(true);
            await request.ConfigureAwait(true);
        }
        Assert.True(handler.Exited.Task.IsCompletedSuccessfully);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            using var response = await operation.Client.GetAsync(new Uri("https://ca.example/order")).ConfigureAwait(false);
        }).ConfigureAwait(true);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task BufferingRejectsAnUnknownLengthOversizedResponseAsync()
    {
        using var handler = new DevelopmentAcmeHttpHandler { Oversize = true };
        using var operation = new AcmeOperationHttpClient(new Uri("https://ca.example/directory"), TimeSpan.FromSeconds(5), CancellationToken.None, handler);
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            using var response = await operation.Client.GetAsync(new Uri("https://ca.example/order")).ConfigureAwait(false);
        }).ConfigureAwait(true);
        Assert.True(handler.Exited.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CancellationAlsoJoinsResponseBufferingAfterHeadersReturnedAsync()
    {
        using var cancellation = new CancellationTokenSource();
        using var content = new DevelopmentBlockingAcmeContent();
        using var handler = new DevelopmentAcmeHttpHandler { Content = () => content };
        using var operation = new AcmeOperationHttpClient(new Uri("https://ca.example/directory"), TimeSpan.FromSeconds(5), cancellation.Token, handler);
        var request = Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            using var response = await operation.Client.GetAsync(new Uri("https://ca.example/order")).ConfigureAwait(false);
        });
        try { await content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(true);
            await request.ConfigureAwait(true);
        }
        Assert.True(handler.Exited.Task.IsCompletedSuccessfully);
        Assert.True(content.Exited.Task.IsCompletedSuccessfully);
        Assert.Equal(1, handler.Requests);
    }
}
