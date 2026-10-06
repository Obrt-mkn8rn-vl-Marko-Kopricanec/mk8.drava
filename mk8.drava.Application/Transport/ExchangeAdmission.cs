using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Transport;

internal sealed class ExchangeAdmission(ApplicationBootstrap bootstrap) : IDisposable
{
    public SemaphoreSlim Slots { get; } = new(bootstrap.MaxConcurrentExchanges, bootstrap.MaxConcurrentExchanges);
    public void Dispose() => Slots.Dispose();
}
