using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AutomaticRoutePolicyTests
{
    [Fact]
    public async Task ScopedAddressAndGeneratedActionRetainProvenanceAndDrainedOwnershipAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(intent).ConfigureAwait(true);
        var policy = new NoConfPolicy
        {
            Global = new NoConfPolicyPatch { Host = "global.site.example", PathPrefix = "/global" },
            Site = new NoConfPolicyPatch { PathPrefix = "/site" },
            Services = [new NoConfServicePolicy { ServiceId = "svc",
                Profile = new NoConfPolicyPatch { Host = "edge.site.example" },
                Route = new NoConfPolicyPatch { PathPrefix = "/api/", Action = "staticResponse", StaticResponse = new ProxyStaticResponseOptions { StatusCode = 202, Body = "configured" } } }],
        };
        var compiled = NoConfCompilerTests.Compiler().Compile(fixture.Registry.State, NoConfCompilerTests.Baseline(), policy, "site.example", "node");
        var service = compiled.Services["svc"];
        Assert.Equal("edge.site.example", service.Route.Host);
        Assert.Equal("/api/", service.Route.PathPrefix);
        Assert.Equal(RuntimeRouteAction.StaticResponse, service.Route.Action);
        Assert.Equal(202, service.Route.StaticResponse.StatusCode);
        Assert.Equal("configured", service.Route.StaticResponse.Body);
        Assert.Equal("service-profile", service.Provenance["host"]);
        Assert.Equal("route", service.Provenance["pathPrefix"]);
        Assert.Equal("route", service.Provenance["action"]);
        await fixture.Registry.DrainAsync(RegistryTestFixture.Fingerprint, intent.Identity, CancellationToken.None).ConfigureAwait(true);
        var drained = NoConfCompilerTests.Compiler().Compile(fixture.Registry.State, NoConfCompilerTests.Baseline(), policy, "site.example", "node");
        Assert.Empty(drained.Services["svc"].Route.Upstreams);
        Assert.Equal(RuntimeRouteAction.StaticResponse, drained.Services["svc"].Route.Action);
    }

    [Theory]
    [InlineData("*", "/")]
    [InlineData("127.0.0.1", "/")]
    [InlineData("edge.site.example:443", "/")]
    [InlineData("Edge.site.example", "/")]
    [InlineData("edge.site.example.", "/")]
    [InlineData("register.site.example", "/")]
    [InlineData("edge.site.example", "//authority/path")]
    [InlineData("edge.site.example", "/path?query")]
    [InlineData("edge.site.example", "/path#fragment")]
    [InlineData("edge.site.example", "/a/../b")]
    [InlineData("edge.site.example", "/a/%2e%2e/b")]
    [InlineData("edge.site.example", "/bad%escape")]
    [InlineData("edge.site.example", "/bad\\path")]
    [InlineData("edge.site.example", "/line\rbreak")]
    [InlineData("edge.site.example", "/admin")]
    [InlineData("edge.site.example", "/Admin/users")]
    [InlineData("edge.site.example", "/_drava/live")]
    [InlineData("edge.site.example", "/mk8.drava.proxy.v1.ServiceRegistry/Challenge")]
    public void InvalidAddressesAreRejectedEvenBeforeAServiceExists(string host, string path)
    {
        var policy = Policy(new NoConfPolicyPatch { Host = host, PathPrefix = path });
        Assert.Throws<InvalidDataException>(() => CompileEmpty(policy));
    }

    [Theory]
    [InlineData("/api", "/api", true)]
    [InlineData("/api", "/api2", true)]
    [InlineData("/api/", "/api/v1", true)]
    [InlineData("/api/", "/api2/", false)]
    [InlineData("/a/", "/b/", false)]
    public void FutureServiceClaimsFollowTheNativePrefixMatcher(string first, string second, bool conflict)
    {
        var policy = new NoConfPolicy { Services = [Service("first", first), Service("second", second)] };
        if (conflict) Assert.Throws<InvalidDataException>(() => CompileEmpty(policy));
        else Assert.Empty(CompileEmpty(policy).Services);
    }

    [Theory]
    [InlineData("*", "/", true)]
    [InlineData("*", "/elsewhere/", false)]
    [InlineData("edge.site.example:443", "/api", true)]
    [InlineData("edge.site.example", "/elsewhere/", false)]
    public void ManualClaimsCannotShadowAutomaticOwnership(string host, string path, bool conflict)
    {
        var baseline = NoConfCompilerTests.Baseline();
        var manual = ProxyConfigurationRuntimeMapper.ToRuntimeRoutes([new ProxyRouteOptions { Name = "manual", Host = host, PathPrefix = path, Action = "staticResponse" }], new ProxyOperationalOptions());
        baseline = baseline.WithListenersAndRoutes(baseline.Listeners, manual);
        var policy = Policy(new NoConfPolicyPatch { Host = "edge.site.example", PathPrefix = "/api/" });
        if (conflict) Assert.Throws<InvalidDataException>(() => NoConfCompilerTests.Compiler().Compile(RegistryState.Empty, baseline, policy, "site.example", "node"));
        else Assert.Collection(NoConfCompilerTests.Compiler().Compile(RegistryState.Empty, baseline, policy, "site.example", "node").Snapshot.Routes, static route => Assert.Equal("manual", route.Name));
        Assert.Collection(NoConfCompilerTests.Compiler().Compile(RegistryState.Empty, baseline, policy with { Mode = "manual" }, "site.example", "node").Snapshot.Routes, static route => Assert.Equal("manual", route.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("fileServer")]
    [InlineData("redirect")]
    public void UnsupportedOrIncompleteActionsCannotBecomeConfigured(string action)
    {
        Assert.Throws<InvalidDataException>(() => CompileEmpty(Policy(new NoConfPolicyPatch { Action = action })));
    }

    private static NoConfServicePolicy Service(string id, string path) => new() { ServiceId = id, Route = new NoConfPolicyPatch { Host = "edge.site.example", PathPrefix = path } };
    private static NoConfPolicy Policy(NoConfPolicyPatch patch) => new() { Services = [new NoConfServicePolicy { ServiceId = "svc", Route = patch }] };
    private static CompiledNoConfSnapshot CompileEmpty(NoConfPolicy policy) => NoConfCompilerTests.Compiler().Compile(RegistryState.Empty, NoConfCompilerTests.Baseline(), policy, "site.example", "node");
}
