using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal sealed class SilentProxyConfigurationReloadEventSink : IProxyConfigurationReloadEventSink
{
    public static SilentProxyConfigurationReloadEventSink Instance { get; } = new();

    private SilentProxyConfigurationReloadEventSink()
    {
    }

    public void LoadFailed(string sourceDirectory, IReadOnlyList<string> errors)
    {
    }

    public void Loaded(int version, string sourceDirectory)
    {
    }
}
