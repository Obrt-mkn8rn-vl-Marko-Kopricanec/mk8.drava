using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.NoConf;

public sealed record CompiledNoConfService
{
    public CompiledNoConfService(string serviceId, RuntimeRoute route, IReadOnlyDictionary<string, string> provenance)
    {
        Registry.RegistryNames.RequireLabel(serviceId);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(provenance);
        ServiceId = serviceId;
        Route = route;
        Provenance = RuntimeList.CopyDictionary(provenance, StringComparer.Ordinal);
    }
    public string ServiceId { get; }
    public RuntimeRoute Route { get; }
    public IReadOnlyDictionary<string, string> Provenance { get; }
}
