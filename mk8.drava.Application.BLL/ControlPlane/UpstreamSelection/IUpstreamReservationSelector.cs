namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

public interface IUpstreamReservationSelector
{
    UpstreamReservation? Reserve(UpstreamSelectionRoute route, string? affinityKey);
}
