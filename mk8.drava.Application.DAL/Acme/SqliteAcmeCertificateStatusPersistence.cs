using System.Security.Cryptography;
using System.Text;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;

namespace Mk8.Drava.Application.DAL.Acme;

// This adapter borrows the controller's already-owned, fenced repository; it never opens a second writer.
public sealed class SqliteAcmeCertificateStatusPersistence : IAcmeCertificateStatusPersistence
{
    private readonly SqliteRegistryRepository _repository;
    private readonly string _domain;
    private readonly string _scope;

    public SqliteAcmeCertificateStatusPersistence(SqliteRegistryRepository repository, string domain, Uri directory)
    {
        ArgumentNullException.ThrowIfNull(repository); ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        if (domain.Length > 253 || !domain.Contains('.', StringComparison.Ordinal)) throw new ArgumentException("ACME history requires a canonical site domain.", nameof(domain));
        foreach (var label in domain.Split('.')) RegistryNames.RequireLabel(label);
        if (!directory.IsAbsoluteUri || !string.Equals(directory.Scheme, "https", StringComparison.Ordinal) || directory.UserInfo.Length != 0 ||
            directory.Query.Length != 0 || directory.Fragment.Length != 0) throw new ArgumentException("ACME history requires the approved HTTPS directory.", nameof(directory));
        _repository = repository; _domain = domain;
        _scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(domain + "\n" + directory.AbsoluteUri)));
    }

    public async ValueTask<IReadOnlyList<AcmeCertificateLifecycleStatus>> ReadAsync(CancellationToken cancellationToken)
    {
        var status = await _repository.ReadAcmeStatusAsync(_scope, cancellationToken).ConfigureAwait(false);
        if (status is null) return Array.Empty<AcmeCertificateLifecycleStatus>();
        Validate(status);
        return Array.AsReadOnly(new[] { status });
    }

    public ValueTask UpsertAsync(AcmeCertificateLifecycleStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status); Validate(status);
        return _repository.UpsertAcmeStatusAsync(_scope, status, cancellationToken);
    }

    private void Validate(AcmeCertificateLifecycleStatus status)
    {
        if (!string.Equals(status.CertificateId, "site", StringComparison.Ordinal) || status.Domains.Count != 2 ||
            !string.Equals(status.Domains[0], _domain, StringComparison.Ordinal) || !string.Equals(status.Domains[1], "*." + _domain, StringComparison.Ordinal) ||
            !status.Enabled || !string.Equals(status.Source, status.Active ? "acme" : "none", StringComparison.Ordinal) ||
            status.Active != (status.NotBeforeUtc.HasValue && status.NotAfterUtc.HasValue) ||
            !status.Active && (status.NotBeforeUtc.HasValue || status.NotAfterUtc.HasValue) ||
            status.Active && status.NotBeforeUtc >= status.NotAfterUtc ||
            status.LastResult is not ("attempting" or "failed" or "succeeded" or "not-due") ||
            status.ErrorSummary is { } error && (error.Length > 256 || error.Any(char.IsControl)))
            throw new InvalidDataException("ACME history changed its approved certificate scope or status shape.");
        foreach (var date in new[] { status.NotBeforeUtc, status.NotAfterUtc, status.RenewalDueAtUtc, status.LastAttemptAtUtc,
                     status.LastSucceededAtUtc, status.LastFailedAtUtc, status.NextAttemptNotBeforeUtc })
            if (date is { Offset: var offset } && offset != TimeSpan.Zero) throw new InvalidDataException("ACME history requires UTC dates.");
        var retryBase = string.Equals(status.LastResult, "failed", StringComparison.Ordinal) ? status.LastFailedAtUtc : status.LastAttemptAtUtc;
        if (status.RenewalDueAtUtc is null || status.NextAttemptNotBeforeUtc is null ||
            status.LastResult is "attempting" or "failed" or "succeeded" && status.LastAttemptAtUtc is null ||
            string.Equals(status.LastResult, "succeeded", StringComparison.Ordinal) && (!status.Active || status.LastSucceededAtUtc is null) ||
            string.Equals(status.LastResult, "failed", StringComparison.Ordinal) && status.LastFailedAtUtc is null ||
            status.LastResult is "attempting" or "failed" && (status.NextAttemptNotBeforeUtc <= retryBase || status.NextAttemptNotBeforeUtc > retryBase!.Value.AddDays(1)))
            throw new InvalidDataException("ACME history lacks its durable admission or renewal time.");
    }
}
