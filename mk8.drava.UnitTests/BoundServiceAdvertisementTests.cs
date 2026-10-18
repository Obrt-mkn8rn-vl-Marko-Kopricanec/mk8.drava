using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Registration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class BoundServiceAdvertisementTests
{
    [Theory]
    [InlineData("http://127.0.0.1:10001", "http://192.0.2.10:10002", "192.0.2.10")]
    [InlineData("http://0.0.0.0:10001", "http://[::1]:10002", "::1")]
    [InlineData("http://[::]:10001", "http://127.0.0.1:10002", "127.0.0.1")]
    [InlineData("http://named.example:10001", "http://127.0.0.1:10002", "127.0.0.1")]
    public void ExplicitAddressSelectsTheMatchingListener(string first, string second, string advertised)
    {
        using var server = new BoundServer(first, second);
        var result = BoundServiceAdvertisement.Read(server, new DravaRegistrationOptions { AdvertiseAddress = advertised }, outboundAddress: null);
        Assert.Equal(advertised, result.Address);
        Assert.Equal(10002, result.Port);
    }

    [Theory]
    [InlineData("http://0.0.0.0:10001", "::1")]
    [InlineData("http://[::]:10001", "127.0.0.1")]
    public void AnotherAddressFamilyCannotBeAdvertisedWithoutABoundListener(string binding, string address)
    {
        using var server = new BoundServer(binding);
        Assert.Throws<InvalidDataException>(() => BoundServiceAdvertisement.Read(server, new DravaRegistrationOptions { AdvertiseAddress = address }, outboundAddress: null));
        Assert.Throws<InvalidDataException>(() => BoundServiceAdvertisement.Read(server, new DravaRegistrationOptions(), IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("http://0.0.0.0:10001", "127.0.0.1")]
    [InlineData("http://[::]:10001", "::1")]
    public void WildcardUsesTheVerifiedAddressInItsOwnFamily(string binding, string address)
    {
        using var server = new BoundServer(binding);
        var result = BoundServiceAdvertisement.Read(server, new DravaRegistrationOptions { ReadinessPath = "/ready" }, IPAddress.Parse(address));
        Assert.Equal(address, result.Address);
        Assert.Equal(10001, result.Port);
        Assert.Equal("/ready", result.ReadinessPath);
    }

    [Theory]
    [InlineData("http://127.0.0.1:10001")]
    [InlineData("https://user@127.0.0.1:10001")]
    [InlineData("https://127.0.0.1:10001/another-root")]
    public void Http2SelectsAnUnambiguousTlsOrigin(string first)
    {
        using var server = new BoundServer(first, "https://127.0.0.1:10002");
        var result = BoundServiceAdvertisement.Read(server, new DravaRegistrationOptions { UpstreamProtocol = "http2" }, outboundAddress: null);
        Assert.Equal("https", result.Scheme);
        Assert.Equal(10002, result.Port);
    }

    private sealed class BoundServer : IServer
    {
        public BoundServer(params string[] addresses)
        {
            var endpoints = new ServerAddressesFeature();
            foreach (var address in addresses) endpoints.Addresses.Add(address);
            Features.Set<IServerAddressesFeature>(endpoints);
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();
        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => Features.Set<IServerAddressesFeature>(instance: null);
    }
}
