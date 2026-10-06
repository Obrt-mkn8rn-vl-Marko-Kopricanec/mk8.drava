using System.Globalization;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;

namespace Mk8.Drava.Application.INF.Runtime;
public sealed class SystemRequestIdRuntimeIdentitySource : IProxyRequestIdRuntimeIdentitySource
{
    public string RuntimeIdentity => Environment.ProcessId.ToString("x", CultureInfo.InvariantCulture);
}
