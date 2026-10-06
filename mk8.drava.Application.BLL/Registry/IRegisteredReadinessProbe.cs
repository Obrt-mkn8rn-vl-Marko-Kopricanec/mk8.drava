namespace Mk8.Drava.Application.BLL.Registry;

public interface IRegisteredReadinessProbe
{
    ValueTask<bool> CheckAsync(InstanceIntent intent, Configuration.RuntimeUpstream upstream, CancellationToken cancellationToken);
}
