using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
public sealed record ProxyRuntimePreflightDirectoryRequirement
{
    public ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind Kind, string Name, string RelativePath, bool Critical)
    {
        ProxyStatusFacts.RequireText(Name, nameof(Name));
        ProxyStatusFacts.RequireText(RelativePath, nameof(RelativePath));
        this.Kind = Kind;
        this.Name = Name;
        this.RelativePath = RelativePath;
        this.Critical = Critical;
    }

    public ProxyRuntimePreflightDirectoryKind Kind { get; }
    public string Name { get; }
    public string RelativePath { get; }
    public bool Critical { get; }
}
