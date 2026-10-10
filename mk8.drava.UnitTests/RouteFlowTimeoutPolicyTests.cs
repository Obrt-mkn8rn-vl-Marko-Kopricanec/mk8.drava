using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RouteFlowTimeoutPolicyTests
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private const string ScopedPolicy = """
        {
          "Global": { "Limits": { "ClientRequestBodyIdleTimeoutMs": 1100, "UpstreamConnectTimeoutMs": 1200 } },
          "Site": { "Limits": { "UpstreamResponseBodyIdleTimeoutMs": 3600000, "DownstreamWriteTimeoutMs": 1300 } },
          "Services": [{ "ServiceId": "svc",
            "Profile": { "Limits": { "ClientRequestBodyIdleTimeoutMs": 1400 } },
            "Route": { "Limits": { "DownstreamWriteTimeoutMs": 1500 } }
          }]
        }
        """;

    [Fact]
    public async Task AutomaticRoutesInheritEachFlowBudgetIndependentlyAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var baseline = NoConfCompilerTests.Baseline();
        var compiled = NoConfCompilerTests.Compiler().Compile(fixture.Registry.State, baseline, ReadPolicy(ScopedPolicy), "site.example", "node");
        var route = compiled.Services["svc"].Route;
        var effective = ProxyTimeoutPolicy.ApplyRouteTimeouts(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), baseline.Timeouts);
        Assert.Equal(TimeSpan.FromMilliseconds(1400), effective.ClientRequestBodyIdleTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), effective.UpstreamConnectTimeout);
        Assert.Equal(TimeSpan.FromHours(1), effective.UpstreamResponseBodyIdleTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), effective.DownstreamWriteTimeout);
        Assert.Equal(baseline.Timeouts.TlsHandshakeTimeout, effective.TlsHandshakeTimeout);
        Assert.Equal(baseline.Limits.MaxRequestBodyBytes, route.ResolvedOptions.MaxRequestBodyBytes);
        Assert.False(route.Cache.Enabled);
        Assert.False(route.Retry.Enabled);
    }

    [Fact]
    public void ManualSiteAndRouteBudgetsMergeWithoutReplacingUnspecifiedFields()
    {
        const string json = """
            { "Name":"streaming", "Host":"stream.site.example",
              "Overrides":{"ClientRequestBodyIdleTimeoutMs":1100,"UpstreamConnectTimeoutMs":1200},
              "Routes":[{"Name":"events","PathPrefix":"/events",
                "Overrides":{"UpstreamResponseBodyIdleTimeoutMs":3600000,"DownstreamWriteTimeoutMs":1300},
                "Upstreams":[{"Name":"origin","Address":"127.0.0.1","Port":12345}]
              }] }
            """;
        var site = JsonSerializer.Deserialize<SiteOptions>(json, ReadOptions);
        Assert.NotNull(site);
        var options = SiteOptionsAggregator.ToProxyOptions([SiteConfigurationSource.FromFile("development-site.json", site)]);
        var route = ProxyConfigurationRuntimeMapper.ToRuntimeRoutes(options.Routes, new ProxyOperationalOptions())[0];
        var effective = ProxyTimeoutPolicy.ApplyRouteTimeouts(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), NoConfCompilerTests.Baseline().Timeouts);
        Assert.Equal(TimeSpan.FromMilliseconds(1100), effective.ClientRequestBodyIdleTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), effective.UpstreamConnectTimeout);
        Assert.Equal(TimeSpan.FromHours(1), effective.UpstreamResponseBodyIdleTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(1300), effective.DownstreamWriteTimeout);
    }

    [Fact]
    public async Task UnconfiguredRoutesKeepAllExistingTimeoutDefaultsAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var baseline = NoConfCompilerTests.Baseline();
        var compiled = NoConfCompilerTests.Compiler().Compile(fixture.Registry.State, baseline, new NoConfPolicy(), "site.example", "node");
        var input = ProxyTimeoutRuntimeMapper.ToPolicyInput(compiled.Services["svc"].Route);
        Assert.Equal(baseline.Timeouts, ProxyTimeoutPolicy.ApplyRouteTimeouts(input, baseline.Timeouts));
    }

    [Fact]
    public async Task RetryAttemptOverridesPreserveTheConfiguredFlowBudgetsAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var baseline = NoConfCompilerTests.Baseline();
        var compiled = NoConfCompilerTests.Compiler().Compile(fixture.Registry.State, baseline, ReadPolicy(ScopedPolicy), "site.example", "node");
        var input = ProxyTimeoutRuntimeMapper.ToPolicyInput(compiled.Services["svc"].Route) with { RetryPerAttemptTimeout = TimeSpan.FromMilliseconds(250) };
        var routeTimeouts = ProxyTimeoutPolicy.ApplyRouteTimeouts(input, baseline.Timeouts);
        var attempt = ProxyTimeoutPolicy.ApplyRetryAttemptTimeout(input, routeTimeouts);
        Assert.Equal(TimeSpan.FromMilliseconds(250), attempt.UpstreamConnectTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(250), attempt.UpstreamResponseHeadTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(1400), attempt.ClientRequestBodyIdleTimeout);
        Assert.Equal(TimeSpan.FromHours(1), attempt.UpstreamResponseBodyIdleTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), attempt.DownstreamWriteTimeout);
    }

    [Theory]
    [InlineData("ClientRequestBodyIdleTimeoutMs", 0)]
    [InlineData("ClientRequestBodyIdleTimeoutMs", 99)]
    [InlineData("ClientRequestBodyIdleTimeoutMs", 86400001)]
    [InlineData("UpstreamResponseBodyIdleTimeoutMs", 0)]
    [InlineData("UpstreamResponseBodyIdleTimeoutMs", 99)]
    [InlineData("UpstreamResponseBodyIdleTimeoutMs", 86400001)]
    [InlineData("DownstreamWriteTimeoutMs", 0)]
    [InlineData("DownstreamWriteTimeoutMs", 99)]
    [InlineData("DownstreamWriteTimeoutMs", 86400001)]
    [InlineData("UpstreamConnectTimeoutMs", 0)]
    [InlineData("UpstreamConnectTimeoutMs", 99)]
    [InlineData("UpstreamConnectTimeoutMs", 600001)]
    public void InvalidFlowBudgetsRejectBeforeAnyServiceRegisters(string field, int value)
    {
        var json = JsonSerializer.Serialize(new { Site = new { Limits = new Dictionary<string, int>(StringComparer.Ordinal) { [field] = value } } });
        var policy = ReadPolicy(json);
        Assert.Throws<InvalidDataException>(() => NoConfCompilerTests.Compiler().Compile(RegistryState.Empty, NoConfCompilerTests.Baseline(), policy, "site.example", "node"));
    }

    private static NoConfPolicy ReadPolicy(string json) => JsonSerializer.Deserialize<NoConfPolicy>(json, ReadOptions)
        ?? throw new InvalidDataException("Development policy is missing.");
}
