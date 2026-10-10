namespace Mk8.Drava.Application.BLL.Configuration;
public static class ProxyConfigurationValidationRules
{
    public static IReadOnlyList<string> ValidateTlsReferences(ProxyOptions options, ProxyOperationalOptions operationalOptions)
    {
        ArgumentNullException.ThrowIfNull(operationalOptions);
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];
        HashSet<string> certificateIds = operationalOptions.Certificates.Select(static certificate => certificate.Id).Where(static id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (operationalOptions.Acme.Enabled)
        {
            foreach (var acmeCertificateId in operationalOptions.Acme.Certificates.Where(static certificate => certificate.Enabled).Select(static certificate => certificate.Id).Where(static id => !string.IsNullOrWhiteSpace(id)))
            {
                certificateIds.Add(acmeCertificateId);
            }
        }

        for (var listenerIndex = 0; listenerIndex < options.Listeners.Count; listenerIndex++)
        {
            var listener = options.Listeners[listenerIndex];
            var prefix = $"Proxy:Listeners:{listenerIndex}";
            ValidateListenerTlsReferences(failures, prefix, listener, certificateIds);
        }

        return failures;
    }

    public static IReadOnlyList<string> ValidateListenerMergeCompatibility(IReadOnlyList<SiteConfigurationSource> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);
        List<string> failures = [];
        Dictionary<string, string> defaultCertificatesByListener = new(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sites)
        {
            foreach (var listener in source.Site.Listeners)
            {
                if (string.IsNullOrWhiteSpace(listener.DefaultCertificateId))
                {
                    continue;
                }

                var key = $"{listener.Name}|{listener.Address}|{listener.Port}|{listener.Transport}";
                if (defaultCertificatesByListener.TryGetValue(key, out var existingCertificateId) && !string.Equals(existingCertificateId, listener.DefaultCertificateId, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{source.Path}: listener '{listener.Name}' has default certificate '{listener.DefaultCertificateId}', but another site on the same listener uses '{existingCertificateId}'.");
                }
                else
                {
                    defaultCertificatesByListener[key] = listener.DefaultCertificateId;
                }
            }
        }

        return failures;
    }
    private static void ValidateListenerTlsReferences(List<string> failures, string prefix, ListenerOptions listener, HashSet<string> certificateIds)
    {
        var isHttp = string.Equals(listener.Transport, "http", StringComparison.OrdinalIgnoreCase);
        var isHttps = string.Equals(listener.Transport, "https", StringComparison.OrdinalIgnoreCase);
        if (!isHttp && !isHttps)
        {
            failures.Add($"{prefix}:Transport must be 'http' or 'https'.");
            return;
        }

        if (isHttp)
        {
            if (!string.IsNullOrWhiteSpace(listener.DefaultCertificateId) || listener.SniCertificates.Count > 0)
            {
                failures.Add($"{prefix} must not configure certificates when Transport is 'http'.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(listener.DefaultCertificateId) && listener.SniCertificates.Count == 0)
        {
            failures.Add($"{prefix} must configure DefaultCertificateId or SniCertificates when Transport is 'https'.");
        }

        if (!string.IsNullOrWhiteSpace(listener.DefaultCertificateId) && !certificateIds.Contains(listener.DefaultCertificateId))
        {
            failures.Add($"{prefix}:DefaultCertificateId references unknown certificate '{listener.DefaultCertificateId}'.");
        }

        ValidateSniReferences(failures, prefix, listener, certificateIds);
    }

    private static void ValidateSniReferences(List<string> failures, string prefix, ListenerOptions listener, HashSet<string> certificateIds)
    {
        HashSet<string> sniHosts = new(StringComparer.OrdinalIgnoreCase);
        for (var bindingIndex = 0; bindingIndex < listener.SniCertificates.Count; bindingIndex++)
        {
            var binding = listener.SniCertificates[bindingIndex];
            var bindingPrefix = $"{prefix}:SniCertificates:{bindingIndex}";
            if (string.IsNullOrWhiteSpace(binding.HostName))
            {
                failures.Add($"{bindingPrefix}:HostName is required.");
            }
            else if (!sniHosts.Add(binding.HostName))
            {
                failures.Add($"{bindingPrefix}:HostName '{binding.HostName}' is duplicated for this listener.");
            }

            if (string.IsNullOrWhiteSpace(binding.CertificateId))
            {
                failures.Add($"{bindingPrefix}:CertificateId is required.");
            }
            else if (!certificateIds.Contains(binding.CertificateId))
            {
                failures.Add($"{bindingPrefix}:CertificateId references unknown certificate '{binding.CertificateId}'.");
            }
        }
    }
}
