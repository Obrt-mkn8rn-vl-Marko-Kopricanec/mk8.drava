using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ProxyConfigLintRuntimeListenerState
{
    public ProxyConfigLintRuntimeListenerState(string identity, string kind, bool active)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Identity = identity;
        Kind = kind;
        Active = active;
    }

    public string Identity { get; }
    public string Kind { get; }
    public bool Active { get; }
}
