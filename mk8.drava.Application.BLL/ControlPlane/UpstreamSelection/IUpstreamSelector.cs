namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
public interface IUpstreamSelector
{
    SelectedUpstream? Select(UpstreamSelectionRoute route);
}
