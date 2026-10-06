using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ProxyRequestMethodPolicyTests
{
    public static void ValidatesHttpMethodTokens()
    {
        AssertEx.True(ProxyRequestMethodPolicy.IsValidMethodToken("GET"));
        AssertEx.True(ProxyRequestMethodPolicy.IsValidMethodToken("custom-token"));
        AssertEx.False(ProxyRequestMethodPolicy.IsValidMethodToken("GE T"));
        AssertEx.False(ProxyRequestMethodPolicy.IsValidMethodToken(""));
    }

    public static void ClassifiesSupportedApplicationMethods()
    {
        var getDecision = ProxyRequestMethodPolicy.ClassifyApplicationMethod("GET");
        var lowerPostDecision = ProxyRequestMethodPolicy.ClassifyApplicationMethod("post");
        AssertEx.True(getDecision is not ProxyRequestApplicationMethodDecision.RejectedDecision);
        AssertEx.True(lowerPostDecision is not ProxyRequestApplicationMethodDecision.RejectedDecision);
    }

    public static void ClassifiesSafeReadMethods()
    {
        AssertEx.True(ProxyRequestMethodPolicy.IsSafeReadMethod("GET"));
        AssertEx.True(ProxyRequestMethodPolicy.IsSafeReadMethod("head"));
        AssertEx.False(ProxyRequestMethodPolicy.IsSafeReadMethod("POST"));
        AssertEx.False(ProxyRequestMethodPolicy.IsSafeReadMethod("DELETE"));
    }

    public static void ClassifiesUnsupportedMethods()
    {
        var traceDecision = ProxyRequestMethodPolicy.ClassifyApplicationMethod("TRACE");
        var connectDecision = ProxyRequestMethodPolicy.ClassifyApplicationMethod("connect");
        AssertEx.True(traceDecision is ProxyRequestApplicationMethodDecision.RejectedDecision);
        AssertEx.True(connectDecision is ProxyRequestApplicationMethodDecision.RejectedDecision);
        var traceRejection = (ProxyRequestApplicationMethodDecision.RejectedDecision)traceDecision;
        var connectRejection = (ProxyRequestApplicationMethodDecision.RejectedDecision)connectDecision;
        AssertEx.Equal(ProxyRequestMethodPolicy.MethodUnsupportedReason, traceRejection.Reason);
        AssertEx.Equal(ProxyRequestMethodPolicy.ConnectUnsupportedReason, connectRejection.Reason);
        AssertEx.True(ProxyRequestMethodPolicy.IsConnectTunnelMethod("CONNECT"));
        AssertEx.True(ProxyRequestMethodPolicy.IsConnectTunnelMethod("connect"));
    }
}
