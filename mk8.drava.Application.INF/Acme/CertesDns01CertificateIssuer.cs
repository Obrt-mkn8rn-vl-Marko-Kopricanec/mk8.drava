using System.Security.Cryptography;
using System.Text.Json;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Pkcs;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;

namespace Mk8.Drava.Application.INF.Acme;

internal sealed class CertesDns01CertificateIssuer : IAcmeCertificateIssuer, IDisposable
{
    private readonly AcmeDns01IssuerPolicy _policy;
    private readonly IAcmeDns01ChallengeProvider _dns;
    private readonly Func<HttpMessageHandler>? _handler;
    private readonly SemaphoreSlim _admission = new(1, 1);

    public CertesDns01CertificateIssuer(AcmeDns01IssuerPolicy policy, IAcmeDns01ChallengeProvider dns, Func<HttpMessageHandler>? handler = null)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(dns);
        policy.Validate();
        _policy = policy with { ContactEmails = Array.AsReadOnly(policy.ContactEmails.ToArray()) };
        _dns = dns; _handler = handler;
    }

    public async ValueTask<AcmeCertificateIssueResult> IssueAsync(AcmeCertificateIssueRequest request, AcmeChallengeStore challengeStore, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(challengeStore);
        cancellationToken.ThrowIfCancellationRequested();
        if (!MatchesPolicy(request)) return AcmeCertificateIssueResult.Failed("ACME request does not match the owner-approved issuer policy.");
        if (!await _admission.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return AcmeCertificateIssueResult.Failed("Another ACME operation is active; retry through the certificate lifecycle.");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_policy.OperationTimeout);
            try { return await IssueOwnedAsync(request, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is AcmeException or HttpRequestException or InvalidDataException or IOException or JsonException or FormatException or CryptographicException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return AcmeCertificateIssueResult.Failed("ACME issuance or exact challenge cleanup was not confirmed; accepted serving material is retained.");
            }
        }
        finally { _admission.Release(); }
    }

    private bool MatchesPolicy(AcmeCertificateIssueRequest request)
    {
        if (!request.TermsAccepted || !string.Equals(request.DirectoryUrl, _policy.Directory.AbsoluteUri, StringComparison.Ordinal) ||
            request.Domains.Count is < 1 or > 2 || request.ContactEmails.Count != _policy.ContactEmails.Count) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var domain in request.Domains)
            if (!seen.Add(domain) || !string.Equals(domain, _policy.SiteDomain, StringComparison.Ordinal) && !string.Equals(domain, "*." + _policy.SiteDomain, StringComparison.Ordinal)) return false;
        for (var i = 0; i < request.ContactEmails.Count; i++)
            if (!string.Equals(request.ContactEmails[i], _policy.ContactEmails[i], StringComparison.Ordinal)) return false;
        return true;
    }

    private async ValueTask<AcmeCertificateIssueResult> IssueOwnedAsync(AcmeCertificateIssueRequest request, CancellationToken cancellationToken)
    {
        await _dns.RecoverAsync(cancellationToken).ConfigureAwait(false);
        var accountKey = await AcmeAccountKeyStore.OpenAsync(_policy.AccountKeyPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        using var operation = new AcmeOperationHttpClient(_policy.Directory, _policy.RequestTimeout, cancellationToken, _handler?.Invoke());
        var context = new AcmeContext(_policy.Directory, accountKey, new AcmeHttpClient(_policy.Directory, operation.Client), badNonceRetryCount: 1);
        var account = await context.NewAccount(_policy.ContactEmails.Select(static email => "mailto:" + email).ToList(), termsOfServiceAgreed: true).ConfigureAwait(false);
        if ((await account.Resource().ConfigureAwait(false)).Status != AccountStatus.Valid)
            throw new InvalidDataException("The ACME account is not valid.");
        var domains = request.Domains.ToList();
        var order = await context.NewOrder(domains).ConfigureAwait(false);
        var resource = await order.Resource().ConfigureAwait(false);
        RequireIdentifiers(resource, domains);
        if (resource.Status is not (OrderStatus.Pending or OrderStatus.Ready)) throw new InvalidDataException("The new ACME order is not issuable.");
        var authorizations = (await order.Authorizations().ConfigureAwait(false)).ToArray();
        if (authorizations.Length > 8 || resource.Status == OrderStatus.Pending && authorizations.Length == 0)
            throw new InvalidDataException("ACME authorizations exceed the bounded approved order.");
        foreach (var authorization in authorizations)
            await AuthorizeAsync(authorization, accountKey, domains, cancellationToken).ConfigureAwait(false);
        await WaitForOrderAsync(order, domains, final: false, cancellationToken).ConfigureAwait(false);
        var certificateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
        var csr = new CertificationRequestBuilder(certificateKey);
        for (var i = 0; i < domains.Count; i++) csr.SubjectAlternativeNames.Add(domains[i]);
        await order.Finalize(csr.Generate()).ConfigureAwait(false);
        await WaitForOrderAsync(order, domains, final: true, cancellationToken).ConfigureAwait(false);
        var chain = await order.Download().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = chain.ToPfx(certificateKey).Build("mk8.drava serving", string.Empty);
        if (bytes.Length is < 128 or > 65536) throw new InvalidDataException("The issued certificate exceeds the protected serving material bound.");
        return AcmeCertificateIssueResult.Issued(bytes);
    }

    private async ValueTask AuthorizeAsync(IAuthorizationContext authorization, IKey accountKey, List<string> domains, CancellationToken cancellationToken)
    {
        var resource = await authorization.Resource().ConfigureAwait(false);
        var identifier = RequireAuthorization(resource, domains);
        if (resource.Status == AuthorizationStatus.Valid) return;
        if (resource.Status != AuthorizationStatus.Pending) throw new InvalidDataException("The ACME authorization is not pending or valid.");
        var challenges = (await authorization.Challenges().ConfigureAwait(false)).ToArray();
        if (challenges.Length > 8) throw new InvalidDataException("The ACME authorization exceeds its challenge bound.");
        IChallengeContext? selected = null;
        foreach (var challenge in challenges)
            if (string.Equals(challenge.Type, "dns-01", StringComparison.Ordinal))
            {
                if (selected is not null) throw new InvalidDataException("ACME returned ambiguous DNS challenges.");
                selected = challenge;
            }
        if (selected is null || selected.Token.Length is < 22 or > 256 || selected.Token.Any(static c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new InvalidDataException("ACME requires one bounded DNS challenge token.");
        var record = await _dns.PublishAsync("_acme-challenge." + identifier, accountKey.DnsTxt(selected.Token), Guid.NewGuid().ToString("N"), cancellationToken).ConfigureAwait(false);
        try
        {
            while (!await _dns.IsPropagatedAsync(record, cancellationToken).ConfigureAwait(false))
                await Task.Delay(_policy.PollInterval, cancellationToken).ConfigureAwait(false);
            await selected.Validate().ConfigureAwait(false);
            while (true)
            {
                resource = await authorization.Resource().ConfigureAwait(false);
                if (!string.Equals(RequireAuthorization(resource, domains), identifier, StringComparison.Ordinal))
                    throw new InvalidDataException("ACME changed its approved authorization identifier.");
                if (resource.Status == AuthorizationStatus.Valid) break;
                if (resource.Status != AuthorizationStatus.Pending) throw new InvalidDataException("ACME rejected the authorization.");
                await Task.Delay(_policy.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Cleanup has its own small deadline so owner cancellation still joins deletion of this exact record.
            using var cleanup = new CancellationTokenSource(_policy.CleanupTimeout);
            await _dns.RemoveAsync(record, cleanup.Token).ConfigureAwait(false);
        }
    }

    private async ValueTask WaitForOrderAsync(IOrderContext order, List<string> domains, bool final, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = await order.Resource().ConfigureAwait(false);
            RequireIdentifiers(resource, domains);
            if (resource.Status == (final ? OrderStatus.Valid : OrderStatus.Ready)) return;
            if (final ? resource.Status != OrderStatus.Processing : resource.Status != OrderStatus.Pending)
                throw new InvalidDataException("ACME returned an unexpected order state.");
            await Task.Delay(_policy.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void RequireIdentifiers(Order resource, List<string> domains)
    {
        if (resource.Identifiers is null || resource.Identifiers.Count != domains.Count) throw new InvalidDataException("ACME changed its order identifiers.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in resource.Identifiers)
            if (identifier.Type != IdentifierType.Dns || !domains.Contains(identifier.Value, StringComparer.Ordinal) || !seen.Add(identifier.Value))
                throw new InvalidDataException("ACME changed its approved DNS identifiers.");
    }

    private static string RequireAuthorization(Authorization resource, List<string> domains)
    {
        if (resource.Identifier is null || resource.Identifier.Type != IdentifierType.Dns ||
            !domains.Contains((resource.Wildcard == true ? "*." : "") + resource.Identifier.Value, StringComparer.Ordinal))
            throw new InvalidDataException("ACME authorization is outside the approved order.");
        return resource.Identifier.Value;
    }

    public void Dispose() => _admission.Dispose();
}
