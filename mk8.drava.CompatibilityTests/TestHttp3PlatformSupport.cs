using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class TestHttp3PlatformSupport
{
    public static RuntimeHttp3PlatformSupport Supported { get; } = RuntimeHttp3PlatformSupport.FromFlags(quicListenerSupported: true, quicConnectionSupported: true);
    public static IRuntimeHttp3PlatformSupportSource SupportedSource { get; } = new FixedRuntimeHttp3PlatformSupportSource(Supported);
    public static IProxyConfigurationHttp3ProjectionSource ProjectionSource { get; } = new ProxyConfigurationHttp3ProjectionSource(SupportedSource);

    public static RuntimeHttp3SupportProjection Project(ProxyConfigurationSnapshot snapshot)
    {
        return ProjectionSource.Project(ProxyHttp3SupportConfigurationSourceMapper.FromSources(snapshot.Listeners, snapshot.Routes));
    }

    private sealed class FixedRuntimeHttp3PlatformSupportSource : IRuntimeHttp3PlatformSupportSource
    {
        private readonly RuntimeHttp3PlatformSupport _support;
        public FixedRuntimeHttp3PlatformSupportSource(RuntimeHttp3PlatformSupport support)
        {
            _support = support;
        }

        public RuntimeHttp3PlatformSupport Read()
        {
            return _support;
        }
    }
}
