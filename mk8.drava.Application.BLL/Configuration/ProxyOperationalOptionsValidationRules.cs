namespace Mk8.Drava.Application.BLL.Configuration;
public static partial class ProxyOperationalOptionsValidationRules
{
    private const int MinimumTimeoutMs = 100;
    private const int MaximumTimeoutMs = 10 * 60 * 1000;
    private const int MaximumFlowTimeoutMs = 24 * 60 * 60 * 1000;
    private const int MinimumDiagnosticsCapacity = 1;
    private const int MaximumDiagnosticsCapacity = 10_000;
    private const long MinimumLogFileBytes = 4 * 1024;
    private const long MaximumLogFileBytes = 1024L * 1024 * 1024;
    private const int MinimumLogFileCount = 1;
    private const int MaximumLogFileCount = 128;
    private static void ValidateTimeout(List<string> failures, string name, int value)
    {
        if (value is < MinimumTimeoutMs or > MaximumTimeoutMs)
        {
            failures.Add($"Proxy operational timeout {name} must be between {MinimumTimeoutMs} and {MaximumTimeoutMs} milliseconds.");
        }
    }

    private static void ValidateFlowTimeout(List<string> failures, string name, int value)
    {
        if (value is < MinimumTimeoutMs or > MaximumFlowTimeoutMs)
            failures.Add($"Proxy operational timeout {name} must be between 100 and 86400000 milliseconds.");
    }

    private static void ValidateConnectionLimits(List<string> failures, ProxyConnectionOptions options)
    {
        if (options.MaxRequestsPerClientConnection is < 1 or > 100_000)
        {
            failures.Add("Proxy operational connection limit MaxRequestsPerClientConnection must be between 1 and 100000.");
        }

        if (options.MaxIdleUpstreamConnectionsPerUpstream is < 0 or > 10_000)
        {
            failures.Add("Proxy operational connection limit MaxIdleUpstreamConnectionsPerUpstream must be between 0 and 10000.");
        }

        if (options.MaxActiveUpgradedTunnels is < 1 or > 100_000)
        {
            failures.Add("Proxy operational connection limit MaxActiveUpgradedTunnels must be between 1 and 100000.");
        }
    }

    private static void ValidateObservability(List<string> failures, ProxyObservabilityOptions options)
    {
        if (options.RecentDiagnosticsCapacity is < MinimumDiagnosticsCapacity or > MaximumDiagnosticsCapacity)
        {
            failures.Add($"Proxy observability setting RecentDiagnosticsCapacity must be between {MinimumDiagnosticsCapacity} and {MaximumDiagnosticsCapacity}.");
        }

        if (options.LogPersistence.MaxFileBytes is < MinimumLogFileBytes or > MaximumLogFileBytes)
        {
            failures.Add($"Proxy observability log persistence setting MaxFileBytes must be between {MinimumLogFileBytes} and {MaximumLogFileBytes}.");
        }

        if (options.LogPersistence.MaxFiles is < MinimumLogFileCount or > MaximumLogFileCount)
        {
            failures.Add($"Proxy observability log persistence setting MaxFiles must be between {MinimumLogFileCount} and {MaximumLogFileCount}.");
        }
    }

    private static void ValidateLimits(List<string> failures, ProxyLimitsOptions options)
    {
        if (options.MaxActiveClientConnections is < 1 or > 1_000_000)
        {
            failures.Add("Proxy limit MaxActiveClientConnections must be between 1 and 1000000.");
        }

        if (options.MaxConcurrentTlsHandshakes is < 1 or > 100_000)
        {
            failures.Add("Proxy limit MaxConcurrentTlsHandshakes must be between 1 and 100000.");
        }

        if (options.RequestsPerMinutePerIp is < 1 or > 1_000_000)
        {
            failures.Add("Proxy limit RequestsPerMinutePerIp must be between 1 and 1000000.");
        }

        if (options.UpgradeRequestsPerMinutePerIp is < 1 or > 1_000_000)
        {
            failures.Add("Proxy limit UpgradeRequestsPerMinutePerIp must be between 1 and 1000000.");
        }

        if (options.MaxRequestHeadBytes is < 1024 or > 1024 * 1024)
        {
            failures.Add("Proxy limit MaxRequestHeadBytes must be between 1024 and 1048576.");
        }

        if (options.MaxHeaderCount is < 1 or > 10_000)
        {
            failures.Add("Proxy limit MaxHeaderCount must be between 1 and 10000.");
        }

        if (options.MaxHeaderLineBytes is < 64 or > 1024 * 1024)
        {
            failures.Add("Proxy limit MaxHeaderLineBytes must be between 64 and 1048576.");
        }

        if (options.MaxRequestBodyBytes is < 0 or > 1L * 1024 * 1024 * 1024 * 1024)
        {
            failures.Add("Proxy limit MaxRequestBodyBytes must be between 0 and 1099511627776.");
        }

        if (options.MaxPathBytes is < 1 or > 1024 * 1024)
        {
            failures.Add("Proxy limit MaxPathBytes must be between 1 and 1048576.");
        }

        if (options.ShutdownGracePeriodSeconds is < 1 or > 3600)
        {
            failures.Add("Proxy limit ShutdownGracePeriodSeconds must be between 1 and 3600.");
        }
    }

    private static void ValidateMetrics(List<string> failures, ProxyMetricsOptions options)
    {
        if (options.PublicMetricsEnabled)
        {
            failures.Add("Proxy metrics PublicMetricsEnabled is not supported in Phase 17; use the protected /admin/proxy/metrics endpoint.");
        }
    }

    private static void ValidateAcme(List<string> failures, ProxyAcmeOptions options, IEnumerable<CertificateOptions> manualCertificates, IProxyRelativeStoragePathPolicy relativeStoragePathPolicy, IProxyUrlSyntaxPolicy urlSyntaxPolicy)
    {
        if (options.RenewBeforeDays is < 1 or > 365)
        {
            failures.Add("Proxy ACME setting RenewBeforeDays must be between 1 and 365.");
        }

        if (options.CheckIntervalMinutes is < 5 or > 1440)
        {
            failures.Add("Proxy ACME setting CheckIntervalMinutes must be between 5 and 1440.");
        }

        if (options.RetryAfterMinutes is < 5 or > 1440)
        {
            failures.Add("Proxy ACME setting RetryAfterMinutes must be between 5 and 1440.");
        }

        if (!relativeStoragePathPolicy.IsSafeRelativePath(options.StoragePath))
        {
            failures.Add("Proxy ACME setting StoragePath must be a relative path under the data-directory certs folder.");
        }

        var directoryUrl = ProxyAcmeDirectoryPolicy.ResolveDirectoryUrl(options);
        if ((options.Enabled || directoryUrl.Length != 0) && !urlSyntaxPolicy.IsAbsoluteHttpsUrl(directoryUrl))
        {
            failures.Add("Enabled Proxy ACME requires an explicitly configured absolute https DirectoryUrl.");
        }

        if (options.Enabled && options.Certificates.Any(static certificate => certificate.Enabled) && !options.TermsAccepted)
        {
            failures.Add("Proxy ACME TermsAccepted must be true before ACME-managed certificates can be issued or renewed.");
        }

        foreach (var contact in options.ContactEmails)
        {
            if (string.IsNullOrWhiteSpace(contact) || ContainsControlCharacter(contact) || !contact.Contains('@', StringComparison.Ordinal))
            {
                failures.Add("Proxy ACME ContactEmails entries must be non-empty email-like values without control characters.");
            }
        }

        HashSet<string> ids = manualCertificates.Select(static certificate => certificate.Id).Where(static id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < options.Certificates.Count; index++)
        {
            var certificate = options.Certificates[index];
            var prefix = $"Proxy:Acme:Certificates:{index}";
            if (string.IsNullOrWhiteSpace(certificate.Id))
            {
                failures.Add($"{prefix}:Id is required.");
            }
            else if (!IsSafeStorageSegment(certificate.Id))
            {
                failures.Add($"{prefix}:Id must contain only letters, digits, dot, dash, or underscore.");
            }
            else if (!ids.Add(certificate.Id))
            {
                failures.Add($"{prefix}:Id '{certificate.Id}' duplicates another manual or ACME certificate id.");
            }

            if (certificate.RenewBeforeDays is < 1 or > 365)
            {
                failures.Add($"{prefix}:RenewBeforeDays must be between 1 and 365.");
            }

            if (certificate.Enabled && certificate.Domains.Count == 0)
            {
                failures.Add($"{prefix}:Domains must contain at least one DNS name when the ACME certificate is enabled.");
            }

            HashSet<string> domains = new(StringComparer.OrdinalIgnoreCase);
            for (var domainIndex = 0; domainIndex < certificate.Domains.Count; domainIndex++)
            {
                var domain = certificate.Domains[domainIndex];
                var domainPrefix = $"{prefix}:Domains:{domainIndex}";
                if (!IsValidAcmeDomain(domain))
                {
                    failures.Add($"{domainPrefix} '{domain}' must be a non-wildcard DNS name without control characters.");
                }
                else if (!domains.Add(domain.Trim()))
                {
                    failures.Add($"{domainPrefix} '{domain}' is duplicated.");
                }
            }
        }
    }

    private static bool IsSafeStorageSegment(string value)
    {
        if (value.Length is 0 or > 128)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidAcmeDomain(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length is> 253 || trimmed.StartsWith("*.", StringComparison.Ordinal) || trimmed.Contains('/', StringComparison.Ordinal) || trimmed.Contains('\\', StringComparison.Ordinal) || ContainsControlCharacter(trimmed))
        {
            return false;
        }

        return trimmed.Contains('.', StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> Validate(ProxyOperationalOptions options, Func<string, string?> readEnvironmentVariable, IProxyAdminUrlPolicy adminUrlPolicy, IProxyRelativeStoragePathPolicy relativeStoragePathPolicy, IProxyUrlSyntaxPolicy urlSyntaxPolicy, IProxyTrustedProxyPolicy trustedProxyPolicy)
    {
        ArgumentNullException.ThrowIfNull(urlSyntaxPolicy);
        ArgumentNullException.ThrowIfNull(relativeStoragePathPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(trustedProxyPolicy);
        ArgumentNullException.ThrowIfNull(adminUrlPolicy);
        List<string> failures = [];
        ValidateTimeout(failures, nameof(options.Timeouts.ClientRequestHeadTimeoutMs), options.Timeouts.ClientRequestHeadTimeoutMs);
        ValidateFlowTimeout(failures, nameof(options.Timeouts.ClientRequestBodyIdleTimeoutMs), options.Timeouts.ClientRequestBodyIdleTimeoutMs);
        ValidateTimeout(failures, nameof(options.Timeouts.UpstreamConnectTimeoutMs), options.Timeouts.UpstreamConnectTimeoutMs);
        ValidateTimeout(failures, nameof(options.Timeouts.UpstreamResponseHeadTimeoutMs), options.Timeouts.UpstreamResponseHeadTimeoutMs);
        ValidateFlowTimeout(failures, nameof(options.Timeouts.UpstreamResponseBodyIdleTimeoutMs), options.Timeouts.UpstreamResponseBodyIdleTimeoutMs);
        ValidateFlowTimeout(failures, nameof(options.Timeouts.DownstreamWriteTimeoutMs), options.Timeouts.DownstreamWriteTimeoutMs);
        ValidateTimeout(failures, nameof(options.Timeouts.TlsHandshakeTimeoutMs), options.Timeouts.TlsHandshakeTimeoutMs);
        ValidateTimeout(failures, nameof(options.Timeouts.ClientKeepAliveIdleTimeoutMs), options.Timeouts.ClientKeepAliveIdleTimeoutMs);
        ValidateTimeout(failures, nameof(options.Timeouts.UpstreamIdleConnectionLifetimeMs), options.Timeouts.UpstreamIdleConnectionLifetimeMs);
        ValidateTimeout(failures, nameof(options.Timeouts.TunnelIdleTimeoutMs), options.Timeouts.TunnelIdleTimeoutMs);
        ValidateConnectionLimits(failures, options.Connections);
        ValidateObservability(failures, options.Observability);
        ValidateLimits(failures, options.Limits);
        ValidateForwardedHeaders(failures, options.ForwardedHeaders, trustedProxyPolicy);
        ValidateCertificates(failures, options.Certificates);
        ValidateAdmin(failures, options.Admin, readEnvironmentVariable, adminUrlPolicy);
        ValidateAcme(failures, options.Acme, options.Certificates, relativeStoragePathPolicy, urlSyntaxPolicy);
        ValidateMetrics(failures, options.Metrics);
        return failures;
    }

    private const int MinimumAuditCapacity = RuntimeAdminSecurityFacts.MinimumAuditCapacity;
    private const int MaximumAuditCapacity = RuntimeAdminSecurityFacts.MaximumAuditCapacity;
    private static bool ContainsControlCharacter(string value)
    {
        return value.Any(char.IsControl);
    }

    private static void ValidateCertificates(List<string> failures, IList<CertificateOptions> certificates)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < certificates.Count; index++)
        {
            var certificate = certificates[index];
            var prefix = $"Proxy:Certificates:{index}";
            if (string.IsNullOrWhiteSpace(certificate.Id))
            {
                failures.Add($"{prefix}:Id is required.");
            }
            else if (!ids.Add(certificate.Id))
            {
                failures.Add($"{prefix}:Id '{certificate.Id}' is duplicated.");
            }

            if (!string.Equals(certificate.Format, "pfx", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{prefix}:Format must be 'pfx' for Phase 5.");
            }

            if (string.IsNullOrWhiteSpace(certificate.Path))
            {
                failures.Add($"{prefix}:Path is required.");
            }

            if (!string.IsNullOrEmpty(certificate.Password) && !string.IsNullOrWhiteSpace(certificate.PasswordEnvironmentVariable))
            {
                failures.Add($"{prefix} must not set both Password and PasswordEnvironmentVariable.");
            }
        }
    }

    private static void ValidateForwardedHeaders(List<string> failures, ProxyForwardedHeadersOptions options, IProxyTrustedProxyPolicy trustedProxyPolicy)
    {
        HashSet<string> entries = new(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < options.TrustedProxies.Count; index++)
        {
            var entry = options.TrustedProxies[index];
            var prefix = $"Proxy:ForwardedHeaders:TrustedProxies:{index}";
            if (string.IsNullOrWhiteSpace(entry))
            {
                failures.Add($"{prefix} must not be empty.");
                continue;
            }

            if (!entries.Add(entry.Trim()))
            {
                failures.Add($"{prefix} '{entry}' is duplicated.");
                continue;
            }

            if (!trustedProxyPolicy.IsValidEntry(entry))
            {
                failures.Add($"{prefix} '{entry}' must be an exact IPv4/IPv6 address or CIDR range.");
            }
        }
    }

    private static void ValidateAdmin(List<string> failures, ProxyAdminOptions options, Func<string, string?> readEnvironmentVariable, IProxyAdminUrlPolicy adminUrlPolicy)
    {
        if (options.RecentAuditCapacity is < MinimumAuditCapacity or > MaximumAuditCapacity)
        {
            failures.Add($"Proxy admin setting RecentAuditCapacity must be between {MinimumAuditCapacity} and {MaximumAuditCapacity}.");
        }

        var tokenResolution = ProxyAdminSecurityTokenPolicy.Resolve(options, readEnvironmentVariable);
        if (options.RequireAuthentication && string.IsNullOrEmpty(tokenResolution.Token))
        {
            failures.Add("Proxy admin RequireAuthentication is true, but no token was provided by Admin:Token " + $"or environment variable '{tokenResolution.TokenEnvironmentVariable}'.");
        }

        if (!string.IsNullOrEmpty(options.Token) && ContainsControlCharacter(options.Token))
        {
            failures.Add("Proxy admin Token must not contain control characters.");
        }

        if (ContainsControlCharacter(tokenResolution.TokenEnvironmentVariable))
        {
            failures.Add("Proxy admin TokenEnvironmentVariable must not contain control characters.");
        }

        var urls = ProxyAdminSecurityTokenPolicy.NormalizeUrls(options.Urls);
        for (var index = 0; index < urls.Count; index++)
        {
            var url = urls[index];
            var prefix = $"Proxy:Admin:Urls:{index}";
            if (!adminUrlPolicy.IsValid(url))
            {
                failures.Add($"{prefix} '{url}' must be an absolute http or https URL.");
            }
        }

        if (urls.Any(adminUrlPolicy.IsNonLocal) && !ProxyAdminSecurityTokenPolicy.IsAuthenticationEnabled(options, readEnvironmentVariable))
        {
            failures.Add("Proxy admin Urls includes a non-local bind address, so Admin:RequireAuthentication must be true with a configured token.");
        }
    }
}
