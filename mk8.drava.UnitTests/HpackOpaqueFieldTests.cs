using System.Text;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class HpackOpaqueFieldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllObsTextOctetsSurviveIncomingAndOutgoingHpackWithoutReplacement(bool response)
    {
        var octets = new byte[128];
        for (var index = 0; index < octets.Length; index++) octets[index] = (byte)(128 + index);
        // A literal x-end field with a non-Huffman value of length128.
        byte[] literal = [0, 5, (byte)'x', (byte)'-', (byte)'e', (byte)'n', (byte)'d', 0x7f, 1, .. octets];
        byte[] wire = response ? [0x88, .. literal] : literal;
        Assert.True(HpackCodec.TryDecodeRequestHeaders(wire, out var fields, out var reason), reason);
        if (response)
        {
            Assert.Collection(fields, field =>
            {
                Assert.Equal(":status", field.Name);
                Assert.Equal("200", field.Value);
            }, CheckOpaqueField);
            Assert.Equal(wire, HpackCodec.EncodeResponseHeaders(200, [fields[1]]));
        }
        else
        {
            Assert.Collection(fields, CheckOpaqueField);
            Assert.Equal(wire, HpackCodec.EncodeRequestHeaders(fields));
        }

        void CheckOpaqueField(Mk8.Drava.Application.BLL.Http.ProxyHeaderField field)
        {
            Assert.Equal("x-end", field.Name);
            Assert.Equal(octets, Encoding.Latin1.GetBytes(field.Value));
        }
    }

    [Theory]
    [InlineData(false, "\u0100")]
    [InlineData(false, "\u2713")]
    [InlineData(false, "\ud800")]
    [InlineData(true, "\u0100")]
    [InlineData(true, "\u2713")]
    [InlineData(true, "\ud800")]
    public void UnrepresentableFieldCharactersFailBeforeTheyCanBecomeQuestionMarks(bool response, string value)
    {
        Assert.Throws<EncoderFallbackException>(() =>
        {
            if (response) HpackCodec.EncodeResponseHeaders(200, [new("x-end", value)]);
            else HpackCodec.EncodeRequestHeaders([new("x-end", value)]);
        });
    }
}
