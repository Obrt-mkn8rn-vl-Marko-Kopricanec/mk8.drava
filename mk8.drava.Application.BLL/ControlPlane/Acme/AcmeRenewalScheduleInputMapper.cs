namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public static class AcmeRenewalScheduleInputMapper
{
    public static AcmeRenewalScheduleInput FromSource(AcmeRenewalScheduleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AcmeRenewalScheduleInput(source.Enabled, source.CheckIntervalMinutes);
    }
}
