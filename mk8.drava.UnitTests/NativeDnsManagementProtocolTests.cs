using System.Text;
using System.Text.Json;
using Mk8.Drava.Application.INF.Dns.Management;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class NativeDnsManagementProtocolTests
{
    [Fact]
    public void BodyBinaryUsesPaddedBase64WhileCredentialAndStatusOriginUseCanonicalBase64url()
    {
        var origin = NativeDnsSession.WireName("site.test");
        var owner = NativeDnsSession.WireName("svc.site.test");
        var body = new ManagementApiRequest(origin) { ExpectedRevision = 9, Changes = [new RrsetChange(owner, 1, 300, [new byte[] { 192, 0, 2, 7 }], [], false)] };
        var bytes = ManagementHttpProtocol.WriteRequest(body);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal("BHNpdGUEdGVzdAA=", json.RootElement.GetProperty("Origin").GetString());
        Assert.Equal("wAACBw==", json.RootElement.GetProperty("Changes")[0].GetProperty("Add")[0].GetString());
        Assert.False(json.RootElement.GetProperty("Changes")[0].GetProperty("Replace").GetBoolean());
        Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        Assert.False(json.RootElement.TryGetProperty("Credential", out _)); Assert.False(json.RootElement.TryGetProperty("Actor", out _));
        Assert.Equal("BHNpdGUEdGVzdAA", ManagementHttpProtocol.EncodeOrigin(origin));
        var credential = Enumerable.Range(0, 32).Select(static number => (byte)number).ToArray();
        Assert.Equal("Bearer AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", ManagementHttpProtocol.EncodeCredential(credential));
        Assert.Equal(credential, ManagementHttpProtocol.DecodeCredential(ManagementHttpProtocol.EncodeCredential(credential)));
        Assert.Throws<FormatException>(() => ManagementHttpProtocol.DecodeOrigin("BHNpdGUEdGVzdAA="));
    }

    [Theory]
    [InlineData("{\"Origin\":\"BHNpdGUEdGVzdAA=\",\"Origin\":\"BHNpdGUEdGVzdAA=\"}")]
    [InlineData("{\"Origin\":\"BHNpdGUEdGVzdAA=\",\"Credential\":\"foreign\"}")]
    [InlineData("{\"origin\":\"BHNpdGUEdGVzdAA=\"}")]
    public void DuplicateUnknownAndIncorrectlyCasedMembersAreRejected(string json)
    {
        Assert.Throws<JsonException>(() => ManagementHttpProtocol.ReadRequest(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void ReceiptsRequireMatchingOperationAndActionState()
    {
        var operation = Guid.NewGuid();
        var reply = new ManagementReply(operation, 10, 17, new string('a', 64), "accepted");
        ManagementHttpProtocol.VerifyReply(reply, "patch", operation);
        Assert.Throws<InvalidDataException>(() => ManagementHttpProtocol.VerifyReply(reply, "patch", Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(() => ManagementHttpProtocol.VerifyReply(reply with { State = "current" }, "patch", operation));
        Assert.Throws<InvalidDataException>(() => ManagementHttpProtocol.VerifyReply(reply, "read", operation));
    }
}
