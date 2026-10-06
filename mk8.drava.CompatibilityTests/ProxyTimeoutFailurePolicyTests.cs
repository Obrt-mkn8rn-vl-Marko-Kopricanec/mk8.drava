using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ProxyTimeoutFailurePolicyTests
{
    public static void ClassifiesForwardingTimeoutFailures()
    {
        var clientBody = ProxyTimeoutFailurePolicy.ClassifyForwardingTimeout(ProxyTimeoutKind.ClientRequestBodyIdle, responseStarted: false);
        var upstreamConnect = ProxyTimeoutFailurePolicy.ClassifyForwardingTimeout(ProxyTimeoutKind.UpstreamConnect, responseStarted: false);
        var upstreamHead = ProxyTimeoutFailurePolicy.ClassifyForwardingTimeout(ProxyTimeoutKind.UpstreamResponseHead, responseStarted: false);
        var upstreamBody = ProxyTimeoutFailurePolicy.ClassifyForwardingTimeout(ProxyTimeoutKind.UpstreamResponseBodyIdle, responseStarted: false);
        var downstreamWrite = ProxyTimeoutFailurePolicy.ClassifyForwardingTimeout(ProxyTimeoutKind.DownstreamWrite, responseStarted: false);
        var afterResponseStarted = ProxyTimeoutFailurePolicy.ClassifyForwardingTimeout(ProxyTimeoutKind.UpstreamConnect, responseStarted: true);
        AssertEx.Equal(408, clientBody.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.ClientRequestBodyTimeout, clientBody.FailureKind);
        AssertEx.Equal(504, upstreamConnect.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.UpstreamConnectTimeout, upstreamConnect.FailureKind);
        AssertEx.Equal(504, upstreamHead.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.UpstreamResponseHeadTimeout, upstreamHead.FailureKind);
        AssertEx.Equal(null, upstreamBody.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.UpstreamResponseBodyTimeout, upstreamBody.FailureKind);
        AssertEx.Equal(null, downstreamWrite.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.ClientDisconnected, downstreamWrite.FailureKind);
        AssertEx.Equal(null, afterResponseStarted.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.UpstreamConnectTimeout, afterResponseStarted.FailureKind);
    }
}
