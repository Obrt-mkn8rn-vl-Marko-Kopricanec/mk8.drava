using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Registration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RegistrationStateOwnershipTests
{
    [Fact]
    public void AcceptedUrlsAreOwnedByTheObservation()
    {
        var urls = new List<string> { "https://svc.site.test/" };
        var state = new DravaRegistrationState();
        state.Accept(new RegistrationStatus { Phase = RegistrationPhase.Ready, LeaseSeconds = 90, AssignedUrls = urls });
        urls.Clear();
        var status = Assert.IsType<RegistrationStatus>(state.Status);
        Assert.Equal("https://svc.site.test/", DevelopmentCardinality.RequireSingle(status.AssignedUrls));
        if (status.AssignedUrls is IList<string> list)
            Assert.Throws<NotSupportedException>(() => list[0] = "https://changed.example/");
        Assert.Equal("https://svc.site.test/", DevelopmentCardinality.RequireSingle(Assert.IsType<RegistrationStatus>(state.Status).AssignedUrls));
    }

    [Fact]
    public void StopInvalidatesReadyAndRejectsLateRenewalButAcceptsDrainAcknowledgment()
    {
        var state = new DravaRegistrationState();
        var ready = new RegistrationStatus { Phase = RegistrationPhase.Ready, LeaseSeconds = 90, AssignedUrls = ["https://svc.site.test/"] };
        state.Accept(ready);
        state.BeginStopping();
        Assert.Null(state.Status);
        state.Accept(ready);
        Assert.Null(state.Status);
        Assert.NotEmpty(state.Failure);
        state.Failed();
        state.Accept(ready);
        Assert.Null(state.Status);
        state.Accept(new RegistrationStatus { Phase = RegistrationPhase.Draining, LeaseSeconds = 90 });
        state.BeginStopping();
        Assert.Equal(RegistrationPhase.Draining, state.Status?.Phase);
        Assert.Empty(state.Failure);
    }
}
