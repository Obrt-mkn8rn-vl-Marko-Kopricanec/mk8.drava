using Mk8.Drava.Application.INF.Acme;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class DnsObservationInputTests
{
    [Theory]
    [InlineData("", 53)]
    [InlineData("resolver.test", 53)]
    [InlineData("0.0.0.0", 53)]
    [InlineData("::", 53)]
    [InlineData("224.0.0.1", 53)]
    [InlineData("255.255.255.255", 53)]
    [InlineData("::ffff:127.0.0.1", 53)]
    [InlineData("2001:db8::53%2", 53)]
    [InlineData("127.0.0.1", 0)]
    [InlineData("127.0.0.1", 65536)]
    public void MissingOrNonUnicastResolversNeverSelectAnImplicitNetworkDestination(string address, int port)
    {
        Assert.Throws<InvalidDataException>(() => new ServiceDnsVerifier(["192.0.2.10"], address, port));
        Assert.Throws<InvalidDataException>(() => new AcmeDns01PropagationVerifier(address, port));
        Assert.Throws<InvalidDataException>(() => Controller(address, port).Validate());
    }

    [Theory]
    [InlineData("192.0.2.53", 53)]
    [InlineData("2001:db8::53", 65535)]
    public void BothAddressFamiliesKeepTheCallerSuppliedObservationEndpoint(string address, int port)
    {
        var endpoint = DnsObservationEndpoint.Parse(address, port);
        Assert.Equal(address, endpoint.Address.ToString());
        Assert.Equal(port, endpoint.Port);
        Controller(address, port).Validate();
    }

    private static ControllerBootstrap Controller(string address, int port) => new()
    {
        Domain = "site.test", CertificateAuthorityPath = Path.GetFullPath("fixture-ca.pfx"), EnrollmentRootFingerprint = new string('A', 64),
        DnsServerAddress = address, DnsServerPort = port,
    };
}
