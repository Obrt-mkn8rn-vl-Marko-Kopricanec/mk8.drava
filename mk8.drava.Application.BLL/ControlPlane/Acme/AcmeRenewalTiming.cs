namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;

public static class AcmeRenewalTiming
{
    public static DateTimeOffset CalculateDueAtUtc(AcmeRenewalActiveCertificate? certificate, int renewBeforeDays, bool lifetimeAware)
    {
        if (certificate is null) return DateTimeOffset.MinValue;
        if (!lifetimeAware) return certificate.NotAfterUtc.AddDays(-renewBeforeDays);
        var lifetime = certificate.NotAfterUtc - certificate.NotBeforeUtc;
        if (lifetime <= TimeSpan.Zero || renewBeforeDays < 1) throw new InvalidDataException("Adaptive renewal requires a positive certificate lifetime and lead time.");
        var lead = TimeSpan.FromTicks(Math.Min(TimeSpan.FromDays(renewBeforeDays).Ticks, lifetime.Ticks / 3));
        return certificate.NotAfterUtc.Subtract(lead);
    }
}
