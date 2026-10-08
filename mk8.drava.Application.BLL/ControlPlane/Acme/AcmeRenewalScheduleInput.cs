namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeRenewalScheduleInput(bool Enabled, int CheckIntervalMinutes, TimeSpan? CheckInterval = null);
