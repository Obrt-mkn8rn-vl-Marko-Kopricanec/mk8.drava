using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("mk8.drava.CompatibilityTests")]
[assembly: InternalsVisibleTo("mk8.drava.UnitTests")]
[assembly: InternalsVisibleTo("mk8.drava.IntegrationTests")]

[assembly: SuppressMessage("Performance", "HLQ006", Scope = "member",
    Target = "~M:Mk8.Drava.Application.INF.Observability.AccessLogEmitter.__LogProxyAccessListenerTransportProtocol10007Struct.GetEnumerator",
    Justification = "The pinned framework LoggerMessage generator emits the IEnumerator return required by its IEnumerable interface; this generated member cannot be changed by the application.")]
