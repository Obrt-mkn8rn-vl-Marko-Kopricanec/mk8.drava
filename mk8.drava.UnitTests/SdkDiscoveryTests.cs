using System.Net;
using Haukcode.Mdns;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Registration;
using Mk8.Drava.Transport.Discovery;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class SdkDiscoveryTests
{
    [Fact]
    public void SdkObservationExpiresOnMonotonicTimeAndClearsAfterConnectionFailure()
    {
        var clock = new RegistryTimeProvider();
        var state = new DravaRegistrationState(clock);
        state.Accept(new RegistrationStatus { Phase = RegistrationPhase.Ready, LeaseSeconds = 90, AssignedUrls = ["https://svc.site.test/"] });
        clock.AdjustUtc(TimeSpan.FromDays(2));
        Assert.Equal(RegistrationPhase.Ready, state.Status?.Phase);
        clock.Advance(TimeSpan.FromSeconds(90));
        Assert.Null(state.Status);
        Assert.NotEmpty(state.Failure);
        state.Accept(new RegistrationStatus { Phase = RegistrationPhase.Checking, LeaseSeconds = 90 });
        Assert.Equal(RegistrationPhase.Checking, state.Status?.Phase);
        Assert.Empty(state.Failure);
        state.Failed();
        Assert.Null(state.Status);
        Assert.NotEmpty(state.Failure);
    }

    [Fact]
    public void CandidateHintsRequireExpectedSiteVersionSubnetAndBoundedDnsMetadata()
    {
        var reader = new MdnsCandidateReader("site", new LocalDiscoveryNetwork(IPAddress.Parse("192.0.2.10"), 24));
        var packet = Response("site", "192.0.2.20");
        var candidate = DevelopmentCardinality.RequireSingle(reader.Read(packet, 123));
        Assert.Equal("192.0.2.20", candidate.Address); Assert.Equal(9443, candidate.Port);
        Assert.Empty(new MdnsCandidateReader("another", new LocalDiscoveryNetwork(IPAddress.Parse("192.0.2.10"), 24)).Read(packet, 123));
        Assert.Empty(reader.Read(packet, 321));
        Assert.Empty(new MdnsCandidateReader("site", new LocalDiscoveryNetwork(IPAddress.Parse("192.0.2.10"), 24)).Read(Response("site", "198.51.100.20"), 123));
        Assert.Empty(reader.Read(new byte[9001], 123));
        Assert.Empty(reader.Read([0, 123, 128, 0, 255, 255, 0, 0, 0, 0, 0, 0], 123));
    }

    [Fact]
    public void RemoteLoopbackHintsNeverBecomeControllerLocalCandidates()
    {
        var network = new LocalDiscoveryNetwork(IPAddress.Parse("192.0.2.10"), 24);
        Assert.Empty(new MdnsCandidateReader("site", network).Read(Response("site", "127.0.0.1"), 123));
        Assert.Equal("127.0.0.1", DevelopmentCardinality.RequireSingle(new MdnsCandidateReader("site", network).Read(Response("site", "127.0.0.1"), 123, responseFromLocalInterface: true)).Address);
    }

    [Fact]
    public void SdkRequiresDeclaredReadinessAndCopiesOwnerCandidateLists()
    {
        using var authority = Mk8.Drava.Application.INF.Publication.LocalSiteCertificateAuthority.Create("site", TimeProvider.System);
        using var root = authority.PublicCertificate;
        using var node = authority.IssueNode("node", ["127.0.0.1"]);
        var seeds = new List<DiscoveryCandidate> { new("127.0.0.1", 9443) };
        var options = new DravaRegistrationOptions { Site = new RegistrationSiteTrust { SiteId = "site", Domain = "site.test", RootFingerprint = authority.Fingerprint,
            RootCertificate = new SecretBytes(root.RawData), NodeCertificate = PortableCertificate.ExportPrivate(node), },
            NodeId = "node", OwnerId = "owner", ServiceId = "svc", ReadinessPath = "/ready", GatewaySeeds = seeds, };
        var copied = options.CopyValidated();
        seeds.Clear();
        Assert.Equal(9443, DevelopmentCardinality.RequireSingle(copied.GatewaySeeds).Port);
        Assert.Throws<InvalidDataException>(() => (options with { ReadinessPath = "" }).CopyValidated());
        Assert.Throws<InvalidDataException>(() => new DiscoveryCandidate("224.0.0.1", 9443));
        Assert.Throws<InvalidDataException>(() => new DiscoveryCandidate("gateway.local", 9443));
    }

    private static byte[] Response(string site, string address)
    {
        var response = new DnsMessage { Id = 123, IsResponse = true };
        const string instance = "drava-test." + MdnsCandidateReader.ServiceType;
        response.Answers.Add(new DnsRecord(instance, DnsRecordType.SRV, DnsClass.IN, 60, DnsEncoder.BuildSrv(0, 0, 9443, "gateway.local.")));
        response.Answers.Add(new DnsRecord(instance, DnsRecordType.TXT, DnsClass.IN, 60, DnsEncoder.BuildTxt(new Dictionary<string, string>(StringComparer.Ordinal) { ["v"] = "1", ["site"] = site })));
        response.Additionals.Add(new DnsRecord("gateway.local.", DnsRecordType.A, DnsClass.IN, 60, IPAddress.Parse(address).GetAddressBytes()));
        return DnsEncoder.Encode(response);
    }
}
