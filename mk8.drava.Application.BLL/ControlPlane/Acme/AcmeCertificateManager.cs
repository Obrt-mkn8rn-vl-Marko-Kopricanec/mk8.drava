using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed class AcmeCertificateManager
{
    private readonly IAcmeRenewalConfigurationSource _configurationSource;
    private readonly IAcmeCertificateActivator _certificateActivator;
    private readonly IMdravaDataDirectoryProvider _dataDirectoryProvider;
    private readonly IAcmeCertificateIssuer _issuer;
    private readonly IAcmeCertificateMaterialWriter _materialWriter;
    private readonly AcmeChallengeStore _challengeStore;
    private readonly AcmeCertificateStatusStore _statusStore;
    private readonly TimeProvider _timeProvider;
    private readonly IProxyAcmeMetricsSink _metrics;
    private readonly IAcmeCertificateRenewalEventSink _events;
    public AcmeCertificateManager(IAcmeRenewalConfigurationSource configurationSource, IAcmeCertificateActivator certificateActivator, IMdravaDataDirectoryProvider dataDirectoryProvider, IAcmeCertificateIssuer issuer, IAcmeCertificateMaterialWriter materialWriter, AcmeChallengeStore challengeStore, AcmeCertificateStatusStore statusStore, TimeProvider timeProvider, IProxyAcmeMetricsSink metrics, IAcmeCertificateRenewalEventSink events)
    {
        _configurationSource = configurationSource;
        _certificateActivator = certificateActivator;
        _dataDirectoryProvider = dataDirectoryProvider;
        _issuer = issuer;
        _materialWriter = materialWriter;
        _challengeStore = challengeStore;
        _statusStore = statusStore;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _events = events;
    }

    public async ValueTask CheckRenewalsAsync(CancellationToken cancellationToken)
    {
        var inputResult = _configurationSource.ReadInput();
        if (inputResult is not AcmeRenewalConfigurationInputReadResult.AvailableResult available || !available.Input.Enabled)
        {
            return;
        }

        var input = available.Input;
        _materialWriter.EnsureLayout(_dataDirectoryProvider.GetDataDirectory(), input.StoragePath);
        var nowUtc = _timeProvider.GetUtcNow();
        foreach (var certificate in input.Certificates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CheckCertificateAsync(input, certificate, nowUtc, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask CheckCertificateAsync(AcmeRenewalConfigurationInput input, AcmeRenewalCertificateInput certificate, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (!certificate.Enabled)
        {
            _statusStore.Upsert(CreateStatus(certificate, certificate.ActiveCertificate, nowUtc, "disabled", null, null));
            return;
        }

        var activeCertificate = certificate.ActiveCertificate;
        var renewalDueAtUtc = CalculateRenewalDueAt(activeCertificate, certificate.RenewBeforeDays);
        var existingStatus = _statusStore.Get(certificate.Id);
        if (existingStatus?.NextAttemptNotBeforeUtc is not null && existingStatus.NextAttemptNotBeforeUtc > nowUtc && (activeCertificate is null || renewalDueAtUtc <= nowUtc))
        {
            _statusStore.Upsert(CopyHistory(CreateStatus(certificate, activeCertificate, nowUtc, existingStatus.LastResult, existingStatus.ErrorSummary, existingStatus.NextAttemptNotBeforeUtc), existingStatus));
            return;
        }

        if (activeCertificate is not null && renewalDueAtUtc > nowUtc)
        {
            _statusStore.Upsert(CopyHistory(CreateStatus(certificate, activeCertificate, nowUtc, "not-due", null, renewalDueAtUtc), existingStatus));
            return;
        }

        var attemptStartedAtUtc = nowUtc;
        _metrics.AcmeRenewalAttempted();
        _statusStore.Upsert(CreateStatus(certificate, activeCertificate, nowUtc, "attempting", null, null, LastAttemptAtUtc: attemptStartedAtUtc));
        var request = new AcmeCertificateIssueRequest(certificate.Id, certificate.Domains, input.DirectoryUrl, input.ContactEmails, input.TermsAccepted);
        AcmeCertificateIssueResult result;
        try
        {
            result = await _issuer.IssueAsync(request, _challengeStore, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)when (exception is not OperationCanceledException)
        {
            result = AcmeCertificateIssueResult.Failed(exception.Message);
        }

        if (result is AcmeCertificateIssueResult.FailedResult failed)
        {
            _metrics.AcmeRenewalFailed();
            var nextAttempt = attemptStartedAtUtc.AddMinutes(input.RetryAfterMinutes);
            _events.RenewalFailed(certificate.Id, failed.ErrorSummary);
            _statusStore.Upsert(CreateStatus(certificate, activeCertificate, nowUtc, "failed", SafeError(failed.ErrorSummary), nextAttempt, LastAttemptAtUtc: attemptStartedAtUtc, LastFailedAtUtc: attemptStartedAtUtc));
            return;
        }

        var issued = (AcmeCertificateIssueResult.IssuedResult)result;
        RuntimeCertificate renewedCertificate;
        try
        {
            renewedCertificate = _materialWriter.WriteAndLoad(new AcmeCertificateMaterialWriteRequest(input.StoragePath, certificate.Id, certificate.Domains, _dataDirectoryProvider.GetDataDirectory(), attemptStartedAtUtc, issued.PfxBytes));
        }
        catch (Exception exception)when (exception is not OperationCanceledException)
        {
            _metrics.AcmeRenewalFailed();
            var nextAttempt = attemptStartedAtUtc.AddMinutes(input.RetryAfterMinutes);
            _statusStore.Upsert(CreateStatus(certificate, activeCertificate, nowUtc, "failed", SafeError(exception.Message), nextAttempt, LastAttemptAtUtc: attemptStartedAtUtc, LastFailedAtUtc: attemptStartedAtUtc));
            return;
        }

        _certificateActivator.Activate(renewedCertificate);
        _metrics.AcmeRenewalSucceeded();
        var renewedActiveCertificate = ToActiveCertificate(renewedCertificate);
        _statusStore.Upsert(CreateStatus(certificate, renewedActiveCertificate, nowUtc, "succeeded", null, CalculateRenewalDueAt(renewedActiveCertificate, certificate.RenewBeforeDays), LastAttemptAtUtc: attemptStartedAtUtc, LastSucceededAtUtc: attemptStartedAtUtc));
    }

    private AcmeCertificateLifecycleStatus CreateStatus(AcmeRenewalCertificateInput certificate, AcmeRenewalActiveCertificate? activeCertificate, DateTimeOffset nowUtc, string result, string? errorSummary, DateTimeOffset? nextAttemptNotBeforeUtc, DateTimeOffset? LastAttemptAtUtc = null, DateTimeOffset? LastSucceededAtUtc = null, DateTimeOffset? LastFailedAtUtc = null)
    {
        var active = activeCertificate is not null;
        var renewalDueAtUtc = CalculateRenewalDueAt(activeCertificate, certificate.RenewBeforeDays);
        return new AcmeCertificateLifecycleStatus(certificate.Id, certificate.Enabled, certificate.Domains, active, active ? "acme" : "none", active ? activeCertificate!.NotBeforeUtc : null, active ? activeCertificate!.NotAfterUtc : null, active ? renewalDueAtUtc : nowUtc, LastAttemptAtUtc, LastSucceededAtUtc, LastFailedAtUtc, nextAttemptNotBeforeUtc, result, errorSummary);
    }

    private static DateTimeOffset CalculateRenewalDueAt(AcmeRenewalActiveCertificate? certificate, int renewBeforeDays)
    {
        if (certificate is null)
        {
            return DateTimeOffset.MinValue;
        }

        return certificate.NotAfterUtc.AddDays(-renewBeforeDays);
    }

    private static AcmeRenewalActiveCertificate ToActiveCertificate(RuntimeCertificate certificate)
    {
        return new AcmeRenewalActiveCertificate(certificate.Certificate.NotBefore.ToUniversalTime(), certificate.Certificate.NotAfter.ToUniversalTime());
    }

    private static string? SafeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var sanitized = new string (error.Where(static character => !char.IsControl(character)).ToArray());
        return sanitized.Length <= 256 ? sanitized : sanitized[..256];
    }

    private static AcmeCertificateLifecycleStatus CopyHistory(AcmeCertificateLifecycleStatus status, AcmeCertificateLifecycleStatus? previous)
    {
        if (previous is null)
        {
            return status;
        }

        return new AcmeCertificateLifecycleStatus(status.CertificateId, status.Enabled, status.Domains, status.Active, status.Source, status.NotBeforeUtc, status.NotAfterUtc, status.RenewalDueAtUtc, previous.LastAttemptAtUtc, previous.LastSucceededAtUtc, previous.LastFailedAtUtc, status.NextAttemptNotBeforeUtc, status.LastResult, status.ErrorSummary);
    }
}
