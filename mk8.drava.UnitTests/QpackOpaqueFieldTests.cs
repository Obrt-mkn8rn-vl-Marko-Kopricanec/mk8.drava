using System.Text;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class QpackOpaqueFieldTests
{
    [Fact]
    public void AllObsTextOctetsSurviveIncomingAndOutgoingQpackWithoutReplacement()
    {
        var octets = new byte[128];
        for (var index = 0; index < octets.Length; index++) octets[index] = (byte)(128 + index);
        // Literal name x-end, a non-Huffman value of length128, zero dynamic references.
        byte[] wire = [0, 0, 0x25, (byte)'x', (byte)'-', (byte)'e', (byte)'n', (byte)'d', 0x7f, 1, .. octets];
        Assert.True(Http3Codec.TryDecodeHeaderBlock(wire, 4096, out var fields, out var reason), reason);
        Assert.Collection(fields, field =>
        {
            Assert.Equal("x-end", field.Name);
            Assert.Equal(octets, Encoding.Latin1.GetBytes(field.Value));
        });
        Assert.Equal(wire, Http3Codec.EncodeHeaderBlock(fields));
    }

    [Theory]
    [InlineData("\u0100")]
    [InlineData("\u2713")]
    [InlineData("\ud800")]
    public void UnrepresentableFieldCharactersFailBeforeTheyCanBecomeQuestionMarks(string value)
    {
        Assert.Throws<EncoderFallbackException>(() => Http3Codec.EncodeHeaderBlock([new("x-end", value)]));
    }
}
