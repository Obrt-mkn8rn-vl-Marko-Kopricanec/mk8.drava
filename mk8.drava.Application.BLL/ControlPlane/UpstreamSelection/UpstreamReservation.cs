using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

public sealed class UpstreamReservation : IDisposable
{
    private readonly DestinationAvailabilityStore _availability;
    private readonly DestinationAvailability _destination;
    private int _released;

    internal UpstreamReservation(SelectedUpstream selection, DestinationAvailabilityStore availability, DestinationAvailability destination)
    {
        Selection = selection;
        _availability = availability;
        _destination = destination;
    }

    public SelectedUpstream Selection { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0) return;
        try { Selection.CircuitBreakerLease.Dispose(); }
        finally { _availability.Release(_destination); }
    }
}
