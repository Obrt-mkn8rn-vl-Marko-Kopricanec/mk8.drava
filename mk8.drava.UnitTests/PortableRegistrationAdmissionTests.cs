using Microsoft.Extensions.DependencyInjection;
using Mk8.Drava.Registration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortableRegistrationAdmissionTests
{
    [Fact]
    public async Task CancelledProtectedLoadCannotRegisterAnyServicesAsync()
    {
        var services = new ServiceCollection();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.AddDravaRegistrationAsync(cancellation.Token)).ConfigureAwait(true);
        Assert.Empty(services);
    }

    [Fact]
    public async Task DuplicateIdentityRefusesBeforeReadingAnyProtectedInputAsync()
    {
        var services = new ServiceCollection();
        var existing = new DravaRegistrationOptions();
        services.AddSingleton(existing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.AddDravaRegistrationAsync(CancellationToken.None)).ConfigureAwait(true);
        Assert.Same(existing, DevelopmentCardinality.RequireSingle(services).ImplementationInstance);
    }
}
