using Mk8.Drava.Application.BLL.Configuration;
using System.Net.Quic;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;
internal interface IHttp3QuicListenerFactory
{
    bool IsSupported { get; }

    ValueTask<QuicListener> ListenAsync(RuntimeListener listener, ProxyConfigurationSnapshot snapshot, CancellationToken cancellationToken);
}
