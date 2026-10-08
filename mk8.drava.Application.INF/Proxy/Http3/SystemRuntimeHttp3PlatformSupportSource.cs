#pragma warning disable CA1416
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using System.Net.Quic;

namespace Mk8.Drava.Application.INF.Proxy.Http3;
public sealed class SystemRuntimeHttp3PlatformSupportSource : IRuntimeHttp3PlatformSupportSource
{
    public RuntimeHttp3PlatformSupport Read()
    {
        try
        {
            return RuntimeHttp3PlatformSupport.FromFlags(QuicListener.IsSupported, QuicConnection.IsSupported);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or System.ComponentModel.Win32Exception)
        {
            return RuntimeHttp3PlatformSupport.Unknown;
        }
    }
}
