using System.Text.Json;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Contracts.Administration.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RouteFlowProjectionTests
{
    [Fact]
    public void OriginalConstructorsAndDefaultAdministrationBytesRemainAvailable()
    {
        var options = new RuntimeRouteResolvedOptions(1234, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), AccessLogEnabled: true);
        var projection = new RuntimeRouteResolvedOptionsProjection(1234, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), AccessLogEnabled: true);
        Assert.True(options.FlowTimeouts.IsEmpty);
        Assert.True(projection.FlowTimeouts.IsEmpty);
        var response = RuntimeRouteResolvedOptionsResponseMapper.FromProjection(projection);
        Assert.Null(response.FlowTimeouts);
        var expected = JsonSerializer.Serialize(new
        {
            MaxRequestBodyBytes = 1234L,
            ClientRequestHeadTimeout = TimeSpan.FromSeconds(1),
            UpstreamResponseHeadTimeout = TimeSpan.FromSeconds(2),
            AccessLogEnabled = true,
        });
        Assert.Equal(expected, JsonSerializer.Serialize(response));
        Type[] parameters = [typeof(long), typeof(TimeSpan), typeof(TimeSpan), typeof(bool)];
        Assert.NotNull(typeof(RuntimeRouteResolvedOptions).GetConstructor(parameters));
        Assert.NotNull(typeof(RuntimeRouteResolvedOptionsProjection).GetConstructor(parameters));
        Assert.NotNull(typeof(RuntimeRouteResolvedOptionsResponse).GetConstructor(parameters));
        var (body, request, upstream, logging) = response;
        Assert.Equal(1234, body);
        Assert.Equal(TimeSpan.FromSeconds(1), request);
        Assert.Equal(TimeSpan.FromSeconds(2), upstream);
        Assert.True(logging);
    }

    [Fact]
    public void ConfiguredAdministrationProjectionRoundTripsItsIndependentBudgets()
    {
        var flow = new RuntimeRouteTimeoutOverrides(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromHours(1), TimeSpan.FromSeconds(3));
        var projection = new RuntimeRouteResolvedOptionsProjection(1234, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), AccessLogEnabled: true, flow);
        var response = RuntimeRouteResolvedOptionsResponseMapper.FromProjection(projection);
        var restored = JsonSerializer.Deserialize<RuntimeRouteResolvedOptionsResponse>(JsonSerializer.Serialize(response));
        Assert.NotNull(restored);
        Assert.NotNull(restored.FlowTimeouts);
        Assert.Equal(flow.ClientRequestBodyIdleTimeout, restored.FlowTimeouts.ClientRequestBodyIdleTimeout);
        Assert.Equal(flow.UpstreamConnectTimeout, restored.FlowTimeouts.UpstreamConnectTimeout);
        Assert.Equal(flow.UpstreamResponseBodyIdleTimeout, restored.FlowTimeouts.UpstreamResponseBodyIdleTimeout);
        Assert.Equal(flow.DownstreamWriteTimeout, restored.FlowTimeouts.DownstreamWriteTimeout);
    }

    [Fact]
    public void AServiceFlowProfileDoesNotChangeAnUnrelatedService()
    {
        var policy = new NoConfPolicy
        {
            Services = [new NoConfServicePolicy { ServiceId = "events",
                Route = new NoConfPolicyPatch { Limits = new ProxyRouteOverrideOptions { UpstreamResponseBodyIdleTimeoutMs = 3600000 }, }, }],
        };
        var baseline = NoConfCompilerTests.Baseline();
        RuntimeTimeouts Effective(string service)
        {
            var resolved = NoConfPolicyResolver.Resolve(policy, service, "site.example");
            var route = ProxyConfigurationRuntimeMapper.ToRuntimeRoutes([resolved.Route], new ProxyOperationalOptions())[0];
            return ProxyTimeoutPolicy.ApplyRouteTimeouts(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), baseline.Timeouts);
        }
        Assert.Equal(TimeSpan.FromHours(1), Effective("events").UpstreamResponseBodyIdleTimeout);
        Assert.Equal(baseline.Timeouts, Effective("ordinary"));
    }

    [Fact]
    public void ExplicitMaximumFlowBudgetsKeepTheirExactValues()
    {
        var flow = new RuntimeRouteTimeoutOverrides(TimeSpan.FromDays(1), TimeSpan.FromMinutes(10), TimeSpan.FromDays(1), TimeSpan.FromDays(1));
        var baseline = NoConfCompilerTests.Baseline();
        var input = new ProxyRouteTimeoutPolicyInput(baseline.Timeouts.UpstreamResponseHeadTimeout, RetryPerAttemptTimeout: null) { FlowTimeouts = flow };
        var effective = ProxyTimeoutPolicy.ApplyRouteTimeouts(input, baseline.Timeouts);
        Assert.Equal(TimeSpan.FromDays(1), effective.ClientRequestBodyIdleTimeout);
        Assert.Equal(TimeSpan.FromMinutes(10), effective.UpstreamConnectTimeout);
        Assert.Equal(TimeSpan.FromDays(1), effective.UpstreamResponseBodyIdleTimeout);
        Assert.Equal(TimeSpan.FromDays(1), effective.DownstreamWriteTimeout);
    }
}
