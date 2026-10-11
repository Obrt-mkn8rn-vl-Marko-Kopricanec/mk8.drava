namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
public interface IUpstreamSelector
{
    #pragma warning disable CA1716 // Retain inherited C# instance interface name and all existing selector implementations/callers; no multilingual API rename is intended.
    SelectedUpstream? Select(UpstreamSelectionRoute route);
    #pragma warning restore CA1716
}
