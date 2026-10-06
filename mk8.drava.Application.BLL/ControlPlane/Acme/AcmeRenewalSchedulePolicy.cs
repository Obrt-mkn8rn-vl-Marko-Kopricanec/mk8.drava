namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed class AcmeRenewalSchedulePolicy
{
    public TimeSpan ResolveDelay(AcmeRenewalScheduleInputReadResult input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input is AcmeRenewalScheduleInputReadResult.AvailableResult { Input.Enabled: true } available)
        {
            return TimeSpan.FromMinutes(Math.Clamp(available.Input.CheckIntervalMinutes, 5, 1440));
        }

        return TimeSpan.FromHours(12);
    }
}
