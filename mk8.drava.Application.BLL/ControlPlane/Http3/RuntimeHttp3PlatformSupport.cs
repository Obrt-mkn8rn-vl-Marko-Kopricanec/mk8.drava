namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public sealed record RuntimeHttp3PlatformSupport(string RuntimeSupport, bool QuicListenerSupported, bool QuicConnectionSupported)
{
    public static RuntimeHttp3PlatformSupport FromFlags(bool quicListenerSupported, bool quicConnectionSupported)
    {
        return new RuntimeHttp3PlatformSupport(quicListenerSupported && quicConnectionSupported ? "supported" : "unsupported", quicListenerSupported, quicConnectionSupported);
    }

    public static RuntimeHttp3PlatformSupport Unknown { get; } = new("unknown", QuicListenerSupported: false, QuicConnectionSupported: false);
}
