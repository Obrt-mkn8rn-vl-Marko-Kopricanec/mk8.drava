using Mk8.Drava.CompatibilityTests.LegacyApi.Security;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Mk8.Drava.Application.INF.Runtime;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ProxyAdminAuthenticationTestFactory
{
    public static AdminAuthenticationMiddleware CreateMiddleware(RequestDelegate next, IProxyConfigurationStore store, AdminAuditStore audit, ProxyMetrics? metrics = null, TimeProvider? timeProvider = null)
    {
        return new AdminAuthenticationMiddleware(next, new ProxyAdminAuthenticationService(new ProxyAdminSecurityOptionsReader(store), audit, metrics ?? new ProxyMetrics(), SilentProxyAdminAuthenticationEventSink.Instance, timeProvider ?? TimeProvider.System));
    }

    private sealed class SilentProxyAdminAuthenticationEventSink : IProxyAdminAuthenticationEventSink
    {
        public static SilentProxyAdminAuthenticationEventSink Instance { get; } = new();

        private SilentProxyAdminAuthenticationEventSink()
        {
        }

        public void ActiveConfigurationMissing()
        {
        }
    }
}
