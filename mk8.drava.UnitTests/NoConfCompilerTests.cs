using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.INF.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class NoConfCompilerTests
{
    [Fact]
    public async Task RegistryCompilesDirectlyToAnImmutablePoolWithP2cAndNoImplicitCacheOrReplayAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var first = RegistryTestFixture.Intent();
        var second = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(first).ConfigureAwait(true);
        await fixture.ReadyAsync(second).ConfigureAwait(true);
        var compiled = Compiler().Compile(fixture.Registry.State, Baseline(), new NoConfPolicy(), "site.example", "node");
        Assert.Equal(fixture.Registry.State.Revision, compiled.DesiredRevision);
        var route = compiled.Services["svc"].Route;
        Assert.Equal("svc.site.example", route.Host);
        Assert.Equal(BalancingAlgorithm.PowerOfTwoChoices, route.Balancing!.Algorithm);
        Assert.False(route.Cache.Enabled);
        Assert.False(route.Retry.Enabled);
        Assert.Equal(2, route.Upstreams.Count);
        Assert.Contains(route.Upstreams, upstream => upstream.Membership == first.Identity);
        Assert.Contains(route.Upstreams, upstream => upstream.Membership == second.Identity);
        await fixture.Registry.RenewAsync(RegistryTestFixture.Fingerprint, first.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(compiled.DesiredRevision, fixture.Registry.State.Revision);
        await fixture.Registry.DrainAsync(RegistryTestFixture.Fingerprint, first.Identity, CancellationToken.None).ConfigureAwait(true);
        var drained = Compiler().Compile(fixture.Registry.State, Baseline(), new NoConfPolicy(), "site.example", "node");
        Assert.Equal(2, route.Upstreams.Count);
        Assert.Collection(drained.Services["svc"].Route.Upstreams, upstream => Assert.Equal(second.Identity, upstream.Membership));
    }

    [Fact]
    public async Task ScopedPolicyHasExplicitPrecedenceAndCannotRaiseSiteBodyCeilingsAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var policy = new NoConfPolicy
        {
            Global = new NoConfPolicyPatch { Algorithm = BalancingAlgorithm.WeightedRoundRobin },
            Site = new NoConfPolicyPatch { Algorithm = BalancingAlgorithm.LeastActive },
            Services = [new NoConfServicePolicy { ServiceId = "svc", Profile = new NoConfPolicyPatch { Algorithm = BalancingAlgorithm.PowerOfTwoChoices },
                Route = new NoConfPolicyPatch { Algorithm = BalancingAlgorithm.StableHash, AffinityHeader = "X-Tenant", PreferredZone = "local", RequireLocalZone = true,
                    Limits = new ProxyRouteOverrideOptions { MaxRequestBodyBytes = 1234 } } }],
        };
        var compiled = Compiler().Compile(fixture.Registry.State, Baseline(), policy, "site.example", "node");
        var service = compiled.Services["svc"];
        Assert.Equal(BalancingAlgorithm.StableHash, service.Route.Balancing!.Algorithm);
        Assert.Equal("X-Tenant", service.Route.Balancing.AffinityHeader);
        Assert.True(service.Route.Balancing.RequireLocalZone);
        Assert.Equal("route", service.Provenance["balancing"]);
        Assert.Equal("default", service.Provenance["cache"]);
        Assert.Equal(1234, service.Route.ResolvedOptions.MaxRequestBodyBytes);
        var excessive = policy with { Site = new NoConfPolicyPatch { Limits = new ProxyRouteOverrideOptions { MaxRequestBodyBytes = long.MaxValue } }, Services = [] };
        Assert.Throws<InvalidDataException>(() => Compiler().Compile(fixture.Registry.State, Baseline(), excessive, "site.example", "node"));
    }

    [Fact]
    public async Task RemoteLoopbackIsNeverInterpretedAsControllerLocalAndDrainingPoolKeeps503RouteAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(intent).ConfigureAwait(true);
        var remote = Compiler().Compile(fixture.Registry.State, Baseline(), new NoConfPolicy(), "site.example", "another-node");
        Assert.Empty(remote.Services["svc"].Route.Upstreams);
        await fixture.Registry.DrainAsync(RegistryTestFixture.Fingerprint, intent.Identity, CancellationToken.None).ConfigureAwait(true);
        var drained = Compiler().Compile(fixture.Registry.State, Baseline(), new NoConfPolicy(), "site.example", "node");
        Assert.Empty(drained.Services["svc"].Route.Upstreams);
        Assert.Equal("svc.site.example", drained.Services["svc"].Route.Host);
    }

    [Fact]
    public async Task ManualAndAutomaticHostConflictsAndMalformedHeaderPolicyAreRejectedAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var first = Compiler().Compile(fixture.Registry.State, Baseline(), new NoConfPolicy(), "site.example", "node");
        var conflict = Baseline().WithListenersAndRoutes(first.Snapshot.Listeners, first.Snapshot.Routes);
        Assert.Throws<InvalidDataException>(() => Compiler().Compile(fixture.Registry.State, conflict, new NoConfPolicy(), "site.example", "node"));
        var manual = Compiler().Compile(fixture.Registry.State, conflict, new NoConfPolicy { Mode = "manual" }, "site.example", "node");
        Assert.Empty(manual.Services);
        var malformed = new NoConfPolicy { Site = new NoConfPolicyPatch { Headers = new ProxyHeaderPolicyOptions { SetRequestHeaders = [new ProxyHeaderSetOptions { Name = "X-Value", Value = "bad\r\nheader" }] } } };
        Assert.Throws<InvalidDataException>(() => Compiler().Compile(fixture.Registry.State, Baseline(), malformed, "site.example", "node"));
        var invalidSchema = new NoConfPolicy { Version = 2 };
        Assert.Throws<InvalidDataException>(() => Compiler().Compile(RegistryState.Empty, Baseline(), invalidSchema, "site.example", "node"));
    }

    [Fact]
    public void UnusedPoliciesAreValidatedBeforeAnyServiceRegisters()
    {
        var policy = new NoConfPolicy { Services = [new NoConfServicePolicy { ServiceId = "future", Route = new NoConfPolicyPatch { AffinityHeader = "X-Tenant" } }] };
        Assert.Throws<InvalidDataException>(() => Compiler().Compile(RegistryState.Empty, Baseline(), policy, "site.example", "node"));
        var invalid = new NoConfPolicy { Global = null! };
        Assert.Throws<InvalidDataException>(() => Compiler().Compile(RegistryState.Empty, Baseline(), invalid, "site.example", "node"));
    }

    [Fact]
    public void PartialLimitOverridesInheritIndependentFieldsAndExposeTheirSources()
    {
        var policy = new NoConfPolicy
        {
            Global = new NoConfPolicyPatch { Limits = new ProxyRouteOverrideOptions { MaxRequestBodyBytes = 1234, ClientRequestHeadTimeoutMs = 1000 } },
            Site = new NoConfPolicyPatch { Limits = new ProxyRouteOverrideOptions { UpstreamResponseHeadTimeoutMs = 2345 } },
        };
        var resolved = NoConfPolicyResolver.Resolve(policy, "svc", "site.example");
        Assert.Equal(1234, resolved.Route.Overrides.MaxRequestBodyBytes);
        Assert.Equal(1000, resolved.Route.Overrides.ClientRequestHeadTimeoutMs);
        Assert.Equal(2345, resolved.Route.Overrides.UpstreamResponseHeadTimeoutMs);
        Assert.Null(resolved.Route.Overrides.AccessLogEnabled);
        Assert.Equal("global", resolved.Provenance["limits.maxRequestBodyBytes"]);
        Assert.Equal("site", resolved.Provenance["limits.upstreamResponseHeadTimeoutMs"]);
    }

    [Fact]
    public void ARegisteredServiceCannotLoopIntoPublicIngressOrRegistration()
    {
        var guard = new NoConfIngressGuard("node", ["127.0.0.1"], [12345, 9443]);
        Assert.Throws<InvalidDataException>(() => guard.Validate(RegistryTestFixture.Intent()));
        guard.Validate(RegistryTestFixture.Intent(address: "192.0.2.10"));
        var wildcard = new NoConfIngressGuard("node", ["0.0.0.0"], [12345]);
        Assert.Throws<InvalidDataException>(() => wildcard.Validate(RegistryTestFixture.Intent(address: "192.0.2.10")));
    }

    [Theory]
    [InlineData("0.1.2.3")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("255.255.255.255")]
    public void EnrollmentRejectsNonUnicastIpv4Scope(string address)
    {
        Assert.Throws<InvalidDataException>(() => new NodeGrant("node", "owner", RegistryTestFixture.Fingerprint, "svc", [address], 1, 65535, DateTimeOffset.UtcNow.AddDays(1), revoked: false));
    }

    internal static NoConfSnapshotCompiler Compiler() => new(new ProxyEndpointAddressPolicy(), new ProxyUrlSyntaxPolicy());
    internal static ProxyConfigurationSnapshot Baseline() => ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(
        new ProxyOptions { Listeners = [new ListenerOptions { Name = "http", Address = "127.0.0.1", Port = 8080 }] }, new ProxyOperationalOptions(),
        ProxyAdminTokenResolution.None("DRAVA_ADMIN_TOKEN"), new Dictionary<string, RuntimeCertificate>(StringComparer.Ordinal), 1, DateTimeOffset.UtcNow,
        "manual", [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("/development", "/development/config", "/development/sites", "/development/logs", "/development/certs", "/development/state", "/development/config/proxy.json"), [], [], []));
}
