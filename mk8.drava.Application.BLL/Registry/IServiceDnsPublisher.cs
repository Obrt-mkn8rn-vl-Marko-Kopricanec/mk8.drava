namespace Mk8.Drava.Application.BLL.Registry;

public interface IServiceDnsPublisher
{
    ValueTask<bool> EnsureAsync(string host, CancellationToken cancellationToken);
}
