using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public static class AcmeRenewalScheduleSourceMapper
{
    public static AcmeRenewalScheduleSource FromSource(RuntimeAcmeOptions acme)
    {
        ArgumentNullException.ThrowIfNull(acme);
        return new AcmeRenewalScheduleSource(acme.Enabled, acme.CheckIntervalMinutes);
    }
}
