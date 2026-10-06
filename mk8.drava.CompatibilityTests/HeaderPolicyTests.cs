using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class HeaderPolicyTests
{
    public static void FiltersStandardHopByHopHeaders()
    {
        var policy = new HopByHopHeaderPolicy();
        var filtered = policy.FilterForForwarding([new ProxyHeaderField("Host", "example.test"), new ProxyHeaderField("Connection", "close"), new ProxyHeaderField("Keep-Alive", "timeout=5"), new ProxyHeaderField("Upgrade", "websocket")], preserveTransferEncoding: false, preserveTrailer: false);
        AssertEx.Equal(1, filtered.Count);
        AssertEx.Equal("Host", filtered[0].Name);
    }

    public static void FiltersConnectionNominatedHeaders()
    {
        var policy = new HopByHopHeaderPolicy();
        var filtered = policy.FilterForForwarding([new ProxyHeaderField("Host", "example.test"), new ProxyHeaderField("Connection", "x-private"), new ProxyHeaderField("X-Private", "do-not-forward")], preserveTransferEncoding: false, preserveTrailer: false);
        AssertEx.Equal(1, filtered.Count);
        AssertEx.Equal("Host", filtered[0].Name);
    }

    public static void ClassifiesHopByHopNamesAndConnectionTokens()
    {
        var policy = new HopByHopHeaderPolicy();
        var filtered = policy.FilterForForwarding([new ProxyHeaderField("Host", "example.test"), new ProxyHeaderField("Proxy-Connection", "keep-alive"), new ProxyHeaderField("TE", "trailers"), new ProxyHeaderField("Trailer", "expires")], preserveTransferEncoding: false, preserveTrailer: false);
        AssertEx.Equal(1, filtered.Count);
        AssertEx.Equal("Host", filtered[0].Name);
        AssertEx.True(HopByHopHeaderPolicy.IsHopByHopHeader("proxy-connection"));
        AssertEx.True(HopByHopHeaderPolicy.IsHopByHopHeader("TE"));
        AssertEx.True(HopByHopHeaderPolicy.HasConnectionToken([new ProxyHeaderField("Connection", "keep-alive, Upgrade")], "upgrade"));
        AssertEx.False(HopByHopHeaderPolicy.HasConnectionToken([new ProxyHeaderField("Connection", "keep-alive")], "upgrade"));
    }

    public static void ClassifiesManagedHttp1FramingHeaders()
    {
        AssertEx.True(Http1ManagedHeaderPolicy.IsManagedFramingHeader("Content-Length"));
        AssertEx.True(Http1ManagedHeaderPolicy.IsManagedFramingHeader("transfer-encoding"));
        AssertEx.True(Http1ManagedHeaderPolicy.IsManagedFramingHeader("Connection"));
        AssertEx.True(Http1ManagedHeaderPolicy.IsManagedFramingHeader("x-request-id"));
        AssertEx.False(Http1ManagedHeaderPolicy.IsManagedFramingHeader("X-Application"));
    }

    public static void ClassifiesManagedStoredResponseHeaders()
    {
        AssertEx.True(Http1ManagedHeaderPolicy.IsManagedStoredResponseHeader("Age"));
        AssertEx.True(Http1ManagedHeaderPolicy.IsManagedStoredResponseHeader("content-length"));
        AssertEx.True(Http1ManagedHeaderPolicy.IsManagedStoredResponseHeader("Transfer-Encoding"));
        AssertEx.True(Http1ManagedHeaderPolicy.IsManagedStoredResponseHeader("Keep-Alive"));
        AssertEx.True(Http1ManagedHeaderPolicy.IsManagedStoredResponseHeader("x-request-id"));
        AssertEx.False(Http1ManagedHeaderPolicy.IsManagedStoredResponseHeader("Cache-Control"));
    }

    public static void AppliesRequestHeaderMutationPolicy()
    {
        var policy = new ProxyHeaderMutationPolicyInput([new ProxyHeaderField("X-Set", "new")], ["X-Remove"], [], []);
        var forwardedHeaders = new ForwardedHeadersContext("203.0.113.10", "203.0.113.10:443", [new ProxyHeaderField("Forwarded", "for=203.0.113.10;proto=https"), new ProxyHeaderField("X-Forwarded-For", "203.0.113.10")]);
        var result = ProxyHeaderMutationPolicy.ApplyRequestHeaders([new ProxyHeaderField("Host", "example.test"), new ProxyHeaderField("X-Remove", "old"), new ProxyHeaderField("X-Set", "old"), new ProxyHeaderField("Forwarded", "for=10.0.0.1"), new ProxyHeaderField("X-Keep", "yes")], policy, forwardedHeaders);
        AssertEx.True(result.Any(static header => header.Name == "Host" && header.Value == "example.test"));
        AssertEx.True(result.Any(static header => header.Name == "X-Keep" && header.Value == "yes"));
        AssertEx.True(result.Any(static header => header.Name == "X-Set" && header.Value == "new"));
        AssertEx.True(result.Any(static header => header.Name == "Forwarded" && header.Value == "for=203.0.113.10;proto=https"));
        AssertEx.True(result.Any(static header => header.Name == "X-Forwarded-For" && header.Value == "203.0.113.10"));
        AssertEx.False(result.Any(static header => header.Name == "X-Remove"));
        AssertEx.False(result.Any(static header => header.Name == "X-Set" && header.Value == "old"));
        AssertEx.False(result.Any(static header => header.Name == "Forwarded" && header.Value == "for=10.0.0.1"));
    }

    public static void ForwardedHeadersContextCopiesHeaders()
    {
        var headers = new List<ProxyHeaderField>
        {
            new("Forwarded", "for=203.0.113.10;proto=https")
        };
        var context = new ForwardedHeadersContext("203.0.113.10", "203.0.113.10:443", headers);
        headers.Clear();
        AssertEx.Equal("203.0.113.10", context.ResolvedClientAddress);
        AssertEx.Equal("203.0.113.10:443", context.ResolvedClientEndpoint);
        AssertEx.Equal("Forwarded", context.Headers[0].Name);
        AssertEx.False(context.Headers is ProxyHeaderField[], "Forwarded header context should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => new ForwardedHeadersContext(null, null, null!));
        AssertEx.Throws<ArgumentNullException>(() => new ForwardedHeadersContext(null, null, [null!]));
        var unresolved = new ForwardedHeadersContext(null, null, []);
        AssertEx.Equal(null, unresolved.ResolvedClientAddress);
        AssertEx.Equal(null, unresolved.ResolvedClientEndpoint);
    }

    public static void ForwardedAddressPolicyNamesNormalizedAddress()
    {
        var policy = new ProxyForwardedHeadersAddressPolicy();
        var result = policy.NormalizeForwardedFor(["bad", "\"[2001:db8::10]\"", "203.0.113.10"]);
        AssertEx.True(result is ForwardedForNormalizationResult.NormalizedResult);
        var normalized = (ForwardedForNormalizationResult.NormalizedResult)result;
        AssertEx.Equal("2001:db8::10", normalized.ClientAddress);
    }

    public static void ForwardedAddressPolicyNamesMissingAddress()
    {
        var policy = new ProxyForwardedHeadersAddressPolicy();
        var result = policy.NormalizeForwardedFor(["", "not-an-ip"]);
        AssertEx.Equal(ForwardedForNormalizationResult.Missing, result);
    }

    public static void AppliesResponseHeaderMutationPolicy()
    {
        var policy = new ProxyHeaderMutationPolicyInput([], [], [new ProxyHeaderField("X-Set", "new")], ["X-Remove"]);
        var result = ProxyHeaderMutationPolicy.ApplyResponseHeaders([new ProxyHeaderField("Content-Type", "text/plain"), new ProxyHeaderField("X-Remove", "old"), new ProxyHeaderField("X-Set", "old")], policy);
        AssertEx.True(result.Any(static header => header.Name == "Content-Type" && header.Value == "text/plain"));
        AssertEx.True(result.Any(static header => header.Name == "X-Set" && header.Value == "new"));
        AssertEx.False(result.Any(static header => header.Name == "X-Remove"));
        AssertEx.False(result.Any(static header => header.Name == "X-Set" && header.Value == "old"));
    }

    public static void HeaderMutationPolicyInputCopiesCollections()
    {
        var setRequest = new List<ProxyHeaderField>
        {
            new("X-Request", "one")
        };
        var removeRequest = new List<string>
        {
            "X-Remove-Request"
        };
        var setResponse = new List<ProxyHeaderField>
        {
            new("X-Response", "one")
        };
        var removeResponse = new List<string>
        {
            "X-Remove-Response"
        };
        var input = new ProxyHeaderMutationPolicyInput(setRequest, removeRequest, setResponse, removeResponse);
        setRequest[0] = new ProxyHeaderField("X-Request", "two");
        removeRequest[0] = "X-Other-Request";
        setResponse[0] = new ProxyHeaderField("X-Response", "two");
        removeResponse[0] = "X-Other-Response";
        setRequest.Clear();
        removeRequest.Clear();
        setResponse.Clear();
        removeResponse.Clear();
        AssertEx.Equal("one", input.SetRequestHeaders[0].Value);
        AssertEx.Equal("X-Remove-Request", input.RemoveRequestHeaders[0]);
        AssertEx.Equal("one", input.SetResponseHeaders[0].Value);
        AssertEx.Equal("X-Remove-Response", input.RemoveResponseHeaders[0]);
        AssertEx.False(input.SetRequestHeaders is ProxyHeaderField[], "Header mutation request set headers should not expose a mutable array.");
        AssertEx.False(input.RemoveRequestHeaders is string[], "Header mutation request remove headers should not expose a mutable array.");
        AssertEx.False(input.SetResponseHeaders is ProxyHeaderField[], "Header mutation response set headers should not expose a mutable array.");
        AssertEx.False(input.RemoveResponseHeaders is string[], "Header mutation response remove headers should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => new ProxyHeaderMutationPolicyInput([null!], [], [], []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyHeaderMutationPolicyInput([], [null!], [], []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyHeaderMutationPolicyInput([], [], [null!], []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyHeaderMutationPolicyInput([], [], [], [null!]));
    }

    public static void HeaderRuntimeMappersRejectNullInputs()
    {
        AssertEx.Throws<ArgumentNullException>(() => ProxyHeaderMutationRuntimeMapper.ToPolicyInput(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyForwardedHeadersRuntimeMapper.ToListener(null!));
    }
}
