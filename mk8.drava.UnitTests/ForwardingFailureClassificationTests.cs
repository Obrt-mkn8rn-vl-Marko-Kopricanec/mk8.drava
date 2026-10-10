using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ForwardingFailureClassificationTests
{
    [Theory]
    [InlineData(false, ProxyFailureKind.UpstreamConnectFailed)]
    [InlineData(true, ProxyFailureKind.UpstreamPrematureDisconnect)]
    public void ConnectionFailureDependsOnWhetherTheResponseHasStarted(bool responseStarted, ProxyFailureKind expected)
    {
        Assert.Equal(expected, ProxyForwardingFailurePolicy.ClassifyConnectionFailure(responseStarted));
    }

    [Theory]
    [InlineData(false, false, ProxyFailureKind.UpstreamMalformedResponse)]
    [InlineData(true, false, ProxyFailureKind.UpstreamConnectFailed)]
    [InlineData(false, true, ProxyFailureKind.UpstreamPrematureDisconnect)]
    [InlineData(true, true, ProxyFailureKind.UpstreamPrematureDisconnect)]
    public void DisconnectingProtocolFailurePreservesCommittedResponses(bool connectionFailed, bool responseStarted, ProxyFailureKind expected)
    {
        Assert.Equal(expected, ProxyForwardingFailurePolicy.ClassifyDisconnectingProtocolFailure(connectionFailed, responseStarted));
    }
}
