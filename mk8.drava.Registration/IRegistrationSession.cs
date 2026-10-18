using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;

namespace Mk8.Drava.Registration;

internal interface IRegistrationSession : IDisposable
{
    NodeAgentDescriptor? AgentDescriptor { get; }
    ValueTask<RegistrationStatus?> RenewAsync(CancellationToken cancellationToken);
    ValueTask<RegistrationStatus?> DrainAsync(CancellationToken cancellationToken);
}
