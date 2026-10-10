using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ConfigurationTests
{
    public static void ConfigurationFileErrorNamesGlobalAndPathErrors()
    {
        var global = ProxyConfigurationFileError.Global("global failure");
        var path = ProxyConfigurationFileError.ForPath("config/site.json", "path failure");
        AssertEx.Equal<string?>(null, global.Path);
        AssertEx.Equal("global failure", global.Message);
        AssertEx.Equal("config/site.json", path.Path);
        AssertEx.Equal("path failure", path.Message);
        AssertEx.Throws<ArgumentException>(() => ProxyConfigurationFileError.Global(" "));
        AssertEx.Throws<ArgumentException>(() => ProxyConfigurationFileError.ForPath(" ", "path failure"));
        AssertEx.Throws<ArgumentException>(() => ProxyConfigurationFileError.ForPath("config/site.json", " "));
    }

    public static void NormalizeSiteParseResultNamesParsedAndFailedStates()
    {
        var site = new SiteOptions
        {
            Name = "parsed"
        };
        var parsed = ProxyConfigurationNormalizeSiteParseResult.Parsed(site, "{}");
        var failed = ProxyConfigurationNormalizeSiteParseResult.Failed("parse failed");
        AssertEx.True(parsed is ProxyConfigurationNormalizeSiteParseResult.ParsedResult);
        var parsedResult = (ProxyConfigurationNormalizeSiteParseResult.ParsedResult)parsed;
        AssertEx.Equal(site, parsedResult.Site);
        AssertEx.Equal("{}", parsedResult.CanonicalJson);
        AssertEx.True(failed is ProxyConfigurationNormalizeSiteParseResult.FailedResult);
        AssertEx.Equal("parse failed", ((ProxyConfigurationNormalizeSiteParseResult.FailedResult)failed).Error);
    }

    public static void SiteConfigurationSourceFactoriesOwnPathsAndValidateInputs()
    {
        var site = new SiteOptions
        {
            Name = "main"
        };
        var file = SiteConfigurationSource.FromFile("sites/main.json", site);
        var normalize = SiteConfigurationSource.FromNormalizeInput(site);
        var lint = SiteConfigurationSource.FromLintInput(site);
        AssertEx.Equal("sites/main.json", file.Path);
        AssertEx.Equal(site, file.Site);
        AssertEx.Equal(SiteConfigurationSource.NormalizeInputPath, normalize.Path);
        AssertEx.Equal(site, normalize.Site);
        AssertEx.Equal(SiteConfigurationSource.LintInputPath, lint.Path);
        AssertEx.Equal(site, lint.Site);
        AssertEx.Throws<ArgumentException>(() => SiteConfigurationSource.FromFile(" ", site));
        AssertEx.Throws<ArgumentNullException>(() => SiteConfigurationSource.FromFile("sites/main.json", null!));
        AssertEx.Throws<ArgumentNullException>(() => SiteConfigurationSource.FromNormalizeInput(null!));
        AssertEx.Throws<ArgumentNullException>(() => SiteConfigurationSource.FromLintInput(null!));
    }

    private static void VerifyExplicitLongFlowTimeouts()
    {
        var shortTimeout = TimeSpan.FromSeconds(10);
        var formerlyRejectedFlow = TimeSpan.FromMilliseconds(600001);
        var runtime = new RuntimeTimeouts(shortTimeout, formerlyRejectedFlow, shortTimeout, shortTimeout,
            formerlyRejectedFlow, formerlyRejectedFlow, shortTimeout, shortTimeout, shortTimeout, shortTimeout);
        var projection = new RuntimeTimeoutsProjection(shortTimeout, formerlyRejectedFlow, shortTimeout, shortTimeout,
            formerlyRejectedFlow, formerlyRejectedFlow, shortTimeout, shortTimeout, shortTimeout, shortTimeout);
        AssertEx.Equal(formerlyRejectedFlow, runtime.ClientRequestBodyIdleTimeout);
        AssertEx.Equal(formerlyRejectedFlow, runtime.UpstreamResponseBodyIdleTimeout);
        AssertEx.Equal(formerlyRejectedFlow, runtime.DownstreamWriteTimeout);
        AssertEx.Equal(formerlyRejectedFlow, projection.ClientRequestBodyIdleTimeout);
        AssertEx.Equal(formerlyRejectedFlow, projection.UpstreamResponseBodyIdleTimeout);
        AssertEx.Equal(formerlyRejectedFlow, projection.DownstreamWriteTimeout);
    }

    public static void SiteOptionsAggregatorCopiesInputCollections()
    {
        CreateCopyTestListenerAndUpstreams(out var listenerSni, out var upstreamFailures, out var upstreams);
        var removeRequestHeaders = new List<string>
        {
            "X-Remove"
        };
        var siteSetRequestHeader = new ProxyHeaderSetOptions
        {
            Name = "X-Site",
            Value = "site"
        };
        var cacheMethods = new List<string>
        {
            "GET"
        };
        var retryStatusCodes = new List<int>
        {
            502
        };
        var routeSetResponseHeader = new ProxyHeaderSetOptions
        {
            Name = "X-Route",
            Value = "route"
        };
        var routeCacheMethods = new List<string>
        {
            "HEAD"
        };
        var routeRetryStatusCodes = new List<int>
        {
            504
        };
        var site = CreateCopyTestSite(listenerSni, upstreams, siteSetRequestHeader, removeRequestHeaders, cacheMethods, retryStatusCodes, routeSetResponseHeader, routeCacheMethods, routeRetryStatusCodes);
        var aggregated = SiteOptionsAggregator.ToProxyOptions([SiteConfigurationSource.FromFile("sites/home.json", site)]);
        site.Listeners.Clear();
        listenerSni.Add(new SniCertificateOptions { HostName = "api.test", CertificateId = "api-cert" });
        site.Upstreams.Clear();
        upstreamFailures.Add(504);
        removeRequestHeaders.Add("X-Late");
        cacheMethods.Add("POST");
        retryStatusCodes.Add(503);
        routeCacheMethods.Add("PUT");
        routeRetryStatusCodes.Add(500);
        VerifyAggregatedCopyIsolation(aggregated, siteSetRequestHeader, routeSetResponseHeader);
    }

    private static void CreateCopyTestListenerAndUpstreams(out List<SniCertificateOptions> listenerSni, out List<int> upstreamFailures, out List<UpstreamOptions> upstreams)
    {
        listenerSni = [
            new()
            {
                HostName = "home.test",
                CertificateId = "home-cert"
            }
        ];
        upstreamFailures = [
            503
        ];
        upstreams = [
            new()
            {
                Name = "primary",
                Address = "127.0.0.1",
                Port = 5000,
                CircuitBreaker = new ProxyCircuitBreakerOptions
                {
                    Enabled = true,
                    FailureStatusCodes = new System.Collections.ObjectModel.Collection<int>(upstreamFailures)
                }
            }
        ];
    }

    private static void VerifyAggregatedCopyIsolation(ProxyOptions aggregated, ProxyHeaderSetOptions siteSetRequestHeader, ProxyHeaderSetOptions routeSetResponseHeader)
    {
        AssertEx.Equal(1, aggregated.Listeners.Count);
        AssertEx.Equal(1, aggregated.Listeners[0].SniCertificates.Count);
        AssertEx.Equal("home.test", aggregated.Listeners[0].SniCertificates[0].HostName);
        AssertEx.Equal(2, aggregated.Routes.Count);
        AssertEx.Equal(1, aggregated.Routes[0].Upstreams.Count);
        AssertEx.Equal(1, aggregated.Routes[0].Upstreams[0].CircuitBreaker.FailureStatusCodes.Count);
        AssertEx.Equal(503, aggregated.Routes[0].Upstreams[0].CircuitBreaker.FailureStatusCodes[0]);
        AssertEx.Equal(1, aggregated.Routes[0].HeaderPolicy.RemoveRequestHeaders.Count);
        AssertEx.Equal("X-Remove", aggregated.Routes[0].HeaderPolicy.RemoveRequestHeaders[0]);
        AssertEx.Equal(1, aggregated.Routes[0].HeaderPolicy.SetRequestHeaders.Count);
        AssertEx.Equal("X-Site", aggregated.Routes[0].HeaderPolicy.SetRequestHeaders[0].Name);
        AssertEx.False(ReferenceEquals(siteSetRequestHeader, aggregated.Routes[0].HeaderPolicy.SetRequestHeaders[0]));
        AssertEx.Equal(1, aggregated.Routes[0].HeaderPolicy.SetResponseHeaders.Count);
        AssertEx.Equal("X-Route", aggregated.Routes[0].HeaderPolicy.SetResponseHeaders[0].Name);
        AssertEx.False(ReferenceEquals(routeSetResponseHeader, aggregated.Routes[0].HeaderPolicy.SetResponseHeaders[0]));
        AssertEx.Equal(1, aggregated.Routes[0].Cache.Methods.Count);
        AssertEx.Equal("HEAD", aggregated.Routes[0].Cache.Methods[0]);
        AssertEx.Equal(1, aggregated.Routes[0].Retry.RetryOnStatusCodes.Count);
        AssertEx.Equal(504, aggregated.Routes[0].Retry.RetryOnStatusCodes[0]);
        AssertEx.Equal(1, aggregated.Routes[1].Cache.Methods.Count);
        AssertEx.Equal("GET", aggregated.Routes[1].Cache.Methods[0]);
        AssertEx.Equal(1, aggregated.Routes[1].Retry.RetryOnStatusCodes.Count);
        AssertEx.Equal(502, aggregated.Routes[1].Retry.RetryOnStatusCodes[0]);
    }

    private static SiteOptions CreateCopyTestSite(List<SniCertificateOptions> listenerSni, List<UpstreamOptions> upstreams, ProxyHeaderSetOptions siteSetRequestHeader, List<string> removeRequestHeaders, List<string> cacheMethods, List<int> retryStatusCodes, ProxyHeaderSetOptions routeSetResponseHeader, List<string> routeCacheMethods, List<int> routeRetryStatusCodes)
    {
        var site = new SiteOptions
        {
            Name = "home",
            Host = "home.test",
            Listeners = [new ListenerOptions
            {
                Name = "main",
                SniCertificates = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.SniCertificateOptions>(listenerSni)
            }

            ],
            Upstreams = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.UpstreamOptions>(upstreams),
            HeaderPolicy = new ProxyHeaderPolicyOptions
            {
                SetRequestHeaders = [siteSetRequestHeader],
                RemoveRequestHeaders = new System.Collections.ObjectModel.Collection<string>(removeRequestHeaders)
            },
            Cache = new ProxyCachePolicyOptions
            {
                Methods = new System.Collections.ObjectModel.Collection<string>(cacheMethods)
            },
            Retry = new ProxyRetryPolicyOptions
            {
                RetryOnStatusCodes = new System.Collections.ObjectModel.Collection<int>(retryStatusCodes)
            },
            Routes = [new ProxyRouteOptions
            {
                Name = "explicit",
                HeaderPolicy = new ProxyHeaderPolicyOptions
                {
                    SetResponseHeaders = [routeSetResponseHeader]
                },
                Cache = new ProxyCachePolicyOptions
                {
                    Enabled = true,
                    Methods = new System.Collections.ObjectModel.Collection<string>(routeCacheMethods)
                },
                Retry = new ProxyRetryPolicyOptions
                {
                    Enabled = true,
                    RetryOnStatusCodes = new System.Collections.ObjectModel.Collection<int>(routeRetryStatusCodes)
                }
            }, new ProxyRouteOptions
            {
                Name = "fallback"
            }

            ]
        };
        return site;
    }

    public static void SiteOptionsAggregatorCopiesMergedListenerSniEntries()
    {
        var firstSni = new SniCertificateOptions
        {
            HostName = "home.test",
            CertificateId = "home-cert"
        };
        var secondSni = new SniCertificateOptions
        {
            HostName = "api.test",
            CertificateId = "api-cert"
        };
        var first = new SiteOptions
        {
            Name = "home",
            Host = "home.test",
            Listeners = [new ListenerOptions
            {
                Name = "shared",
                Address = "0.0.0.0",
                Port = 443,
                Transport = "tls",
                SniCertificates = [firstSni]
            }

            ]
        };
        var second = new SiteOptions
        {
            Name = "api",
            Host = "api.test",
            Listeners = [new ListenerOptions
            {
                Name = "shared",
                Address = "0.0.0.0",
                Port = 443,
                Transport = "tls",
                SniCertificates = [secondSni]
            }

            ]
        };
        var aggregated = SiteOptionsAggregator.ToProxyOptions([SiteConfigurationSource.FromFile("sites/home.json", first), SiteConfigurationSource.FromFile("sites/api.json", second)]);
        AssertEx.Equal(1, aggregated.Listeners.Count);
        AssertEx.Equal(2, aggregated.Listeners[0].SniCertificates.Count);
        AssertEx.Equal("home.test", aggregated.Listeners[0].SniCertificates[0].HostName);
        AssertEx.Equal("api.test", aggregated.Listeners[0].SniCertificates[1].HostName);
        AssertEx.False(ReferenceEquals(firstSni, aggregated.Listeners[0].SniCertificates[0]));
        AssertEx.False(ReferenceEquals(secondSni, aggregated.Listeners[0].SniCertificates[1]));
    }

    public static void ConfigurationNormalizeResultNamesNormalizedAndFailedOutcomes()
    {
        var normalized = ProxyConfigurationNormalizeResult.Normalized("json", "{}");
        var failed = ProxyConfigurationNormalizeResult.Failed("yaml", [ProxyConfigurationFileError.Global("parse failed")]);
        AssertEx.True(normalized is ProxyConfigurationNormalizeResult.NormalizedResult);
        AssertEx.Equal("json", normalized.Format);
        AssertEx.Equal("{}", ((ProxyConfigurationNormalizeResult.NormalizedResult)normalized).CanonicalJson);
        AssertEx.Equal(0, normalized.Errors.Count);
        AssertEx.Equal(0, normalized.FileErrors.Count);
        AssertEx.True(failed is ProxyConfigurationNormalizeResult.FailedResult);
        AssertEx.Equal("yaml", failed.Format);
        AssertEx.Equal("parse failed", failed.Errors[0]);
        AssertEx.Equal("parse failed", failed.FileErrors[0].Message);
        AssertEx.Throws<ArgumentException>(() => ProxyConfigurationNormalizeResult.Normalized(" ", "{}"));
        AssertEx.Throws<ArgumentException>(() => ProxyConfigurationNormalizeResult.Failed(" ", [ProxyConfigurationFileError.Global("parse failed")]));
        AssertEx.Throws<ArgumentException>(() => ProxyConfigurationNormalizeResult.Failed("json", []));
    }

    public static void ConfigurationReloadResultNamesReloadOutcomes()
    {
        var attemptedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var loadedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(2);
        var discovery = new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []);
        var active = new TestConfigurationProjection("active");
        var listenerReload = ProxyListenerReloadResult.Applied(attemptedAtUtc, added: 1, removed: 0, changed: 0, unchanged: 0, changes: [], errors: []);
        var failedListenerReload = ProxyListenerReloadResult.Failed(attemptedAtUtc, added: 1, removed: 0, changed: 0, unchanged: 0, changes: [], errors: ["listener failed"]);
        AssertEx.True(listenerReload is ProxyListenerReloadResult.AppliedResult);
        AssertEx.True(failedListenerReload is ProxyListenerReloadResult.FailedResult);
        var loadFailed = ProxyConfigurationReloadResult<TestConfigurationProjection>.LoadFailed(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 7, loadedAtUtc: loadedAtUtc, discovery: discovery, errors: ["parse failed"], fileErrors: [ProxyConfigurationFileError.ForPath("sites/broken.json", "parse failed")], activeConfiguration: active);
        var listenerFailed = ProxyConfigurationReloadResult<TestConfigurationProjection>.ListenerReloadFailed(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 7, loadedAtUtc: loadedAtUtc, discovery: discovery, listenerReload: failedListenerReload, activeConfiguration: active);
        var reloaded = ProxyConfigurationReloadResult<TestConfigurationProjection>.Reloaded(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 8, loadedAtUtc: loadedAtUtc, discovery: discovery, listenerReload: listenerReload, activeConfiguration: active);
        var loadFailedResult = ProxyConfigurationReloadResultAssertions.LoadFailed(loadFailed);
        AssertEx.Equal(7, loadFailed.ActiveVersion!.Value);
        AssertEx.Equal(loadedAtUtc, loadFailed.LastSuccessfulLoadAtUtc!.Value);
        AssertEx.Equal("parse failed", loadFailed.Errors[0]);
        AssertEx.Equal("sites/broken.json", loadFailed.FileErrors[0].Path);
        AssertEx.Equal(active, AssertEx.NotNull(loadFailedResult.ActiveConfiguration));
        var listenerFailedResult = ProxyConfigurationReloadResultAssertions.ListenerReloadFailed(listenerFailed);
        AssertEx.Equal(failedListenerReload, listenerFailedResult.ListenerReload);
        AssertEx.Equal("listener failed", listenerFailed.Errors[0]);
        AssertEx.Equal("listener failed", listenerFailed.FileErrors[0].Message);
        var reloadedResult = ProxyConfigurationReloadResultAssertions.Reloaded(reloaded);
        AssertEx.Equal(8, reloaded.ActiveVersion!.Value);
        AssertEx.Equal(0, reloaded.Errors.Count);
        AssertEx.Equal(0, reloaded.FileErrors.Count);
        AssertEx.Equal(listenerReload, reloadedResult.ListenerReload);
        AssertEx.Equal(active, reloadedResult.ActiveConfiguration);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyConfigurationReloadResult<TestConfigurationProjection>.LoadFailed(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 0, loadedAtUtc: loadedAtUtc, discovery: discovery, errors: ["parse failed"], fileErrors: [ProxyConfigurationFileError.ForPath("sites/broken.json", "parse failed")], activeConfiguration: active));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyConfigurationReloadResult<TestConfigurationProjection>.ListenerReloadFailed(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: -1, loadedAtUtc: loadedAtUtc, discovery: discovery, listenerReload: failedListenerReload, activeConfiguration: active));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyConfigurationReloadResult<TestConfigurationProjection>.Reloaded(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 0, loadedAtUtc: loadedAtUtc, discovery: discovery, listenerReload: listenerReload, activeConfiguration: active));
        AssertEx.Throws<ArgumentException>(() => ProxyConfigurationReloadResult<TestConfigurationProjection>.LoadFailed(sourceDirectory: " ", attemptedAtUtc: attemptedAtUtc, activeVersion: 7, loadedAtUtc: loadedAtUtc, discovery: discovery, errors: ["parse failed"], fileErrors: [ProxyConfigurationFileError.ForPath("sites/broken.json", "parse failed")], activeConfiguration: active));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationReloadResult<TestConfigurationProjection>.Reloaded(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 8, loadedAtUtc: loadedAtUtc, discovery: null!, listenerReload: listenerReload, activeConfiguration: active));
    }

    public static void ListenerReloadResultCopiesChangesAndErrors()
    {
        var changes = new List<ProxyListenerReloadChange>
        {
            new("changed", "main", "main|tcp", "127.0.0.1|18080|tcp", "active")
        };
        var errors = new List<string>
        {
            "bind failed"
        };
        var result = ProxyListenerReloadResult.Failed(DateTimeOffset.UnixEpoch, added: 0, removed: 0, changed: 1, unchanged: 0, changes, errors);
        changes.Clear();
        errors.Clear();
        AssertEx.Equal(1, result.Changes.Count);
        AssertEx.Equal("changed", result.Changes[0].Action);
        AssertEx.Equal("main", result.Changes[0].Name);
        AssertEx.False(result.Changes is ProxyListenerReloadChange[], "Listener reload changes should not expose a mutable array.");
        AssertEx.Equal(1, result.Errors.Count);
        AssertEx.Equal("bind failed", result.Errors[0]);
        AssertEx.False(result.Errors is string[], "Listener reload errors should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => ProxyListenerReloadResult.Applied(DateTimeOffset.UnixEpoch, added: 1, removed: 0, changed: 0, unchanged: 0, changes: [null!], errors: []));
        AssertEx.Throws<ArgumentNullException>(() => ProxyListenerReloadResult.Failed(DateTimeOffset.UnixEpoch, added: 0, removed: 0, changed: 1, unchanged: 0, changes, errors: [null!]));
        var response = ProxyListenerReloadResponseMapper.FromResult(result);
        AssertEx.False(response.Changes is ProxyListenerReloadChangeResponse[], "Listener reload API changes should not expose a mutable array.");
        AssertEx.False(response.Errors is string[], "Listener reload API errors should not expose a mutable array.");
        var responseChanges = new List<ProxyListenerReloadChangeResponse>
        {
            new("changed", "main", "main|tcp", "127.0.0.1|18080|tcp", "active", null)
        };
        var responseErrors = new List<string>
        {
            "bind failed"
        };
        var directResponse = new ProxyListenerReloadResponse(succeeded: false, attemptedAtUtc: DateTimeOffset.UnixEpoch, added: 0, removed: 0, changed: 1, unchanged: 0, changes: responseChanges, errors: responseErrors);
        responseChanges[0] = new ProxyListenerReloadChangeResponse("removed", "replacement", "replacement|tcp", "127.0.0.1|19090|tcp", "stopped", null);
        responseErrors[0] = "replacement error";
        responseChanges.Clear();
        responseErrors.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyListenerReloadResponse(succeeded: false, attemptedAtUtc: DateTimeOffset.UnixEpoch, added: 0, removed: 0, changed: 1, unchanged: 0, changes: null!, errors: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyListenerReloadResponse(succeeded: false, attemptedAtUtc: DateTimeOffset.UnixEpoch, added: 0, removed: 0, changed: 1, unchanged: 0, changes: [], errors: null!));
        AssertEx.Equal("changed", directResponse.Changes[0].Action);
        AssertEx.Equal("main", directResponse.Changes[0].Name);
        AssertEx.Equal("bind failed", directResponse.Errors[0]);
        AssertEx.False(directResponse.Changes is ProxyListenerReloadChangeResponse[], "Direct listener reload API changes should not expose a mutable array.");
        AssertEx.False(directResponse.Errors is string[], "Direct listener reload API errors should not expose a mutable array.");
    }

    public static void ApiResultResponseMappersRejectNullResults()
    {
        AssertEx.Throws<ArgumentNullException>(() => ProxyListenerReloadResponseMapper.FromResult(null!));
        AssertEx.Throws<ArgumentNullException>(() => ConfigLintResponseMapper.FromResult(null!));
    }

    public static void ConfigurationManagementResultsCopyInputCollections()
    {
        var discoveredFiles = new List<ProxyConfigurationFileDiscovery>
        {
            new("sites/home.json", "json", "loaded", null)
        };
        var createdPaths = new List<string>
        {
            "tests/config"
        };
        var existingPaths = new List<string>
        {
            "tests/config/sites"
        };
        var discovery = new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), discoveredFiles, createdPaths, existingPaths);
        var replacementFiles = new List<ProxyConfigurationFileDiscovery>
        {
            new("sites/replacement.json", "json", "loaded", null)
        };
        var discoveryWithReplacementFiles = discovery.WithFiles(replacementFiles);
        var sourceFiles = new List<string>
        {
            "sites/home.json"
        };
        var errors = new List<string>
        {
            "parse failed"
        };
        var fileErrors = new List<ProxyConfigurationFileError>
        {
            ProxyConfigurationFileError.ForPath("sites/home.json", "parse failed")
        };
        var normalize = ProxyConfigurationNormalizeResult.Failed("json", fileErrors);
        var valid = ProxyConfigurationValidationResult.Valid(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, lastSuccessfulLoadAtUtc: DateTimeOffset.UnixEpoch, wouldBeVersion: 2, sourceFiles, discovery);
        var invalid = ProxyConfigurationValidationResult.Invalid(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, lastSuccessfulLoadAtUtc: DateTimeOffset.UnixEpoch, wouldBeVersion: null, sourceFiles, discovery, errors, fileErrors);
        var loadFailed = new ProxyConfigurationLoadResult.FailedResult(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, sourceFiles, discovery, fileErrors, wouldBeVersion: null);
        var loadValidated = new ProxyConfigurationLoadResult.ValidatedResult(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, sourceFiles, discovery, wouldBeVersion: 2);
        var reloadFailed = ProxyConfigurationReloadResult<TestConfigurationProjection>.LoadFailed(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, loadedAtUtc: DateTimeOffset.UnixEpoch, discovery, errors, fileErrors, activeConfiguration: null);
        var apiReloadFailed = ProxyConfigurationReloadResult<ProxyConfigurationProjection>.LoadFailed(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, loadedAtUtc: DateTimeOffset.UnixEpoch, discovery, errors, fileErrors, activeConfiguration: null);
        discoveredFiles.Clear();
        createdPaths.Clear();
        existingPaths.Clear();
        replacementFiles.Clear();
        sourceFiles.Clear();
        errors.Clear();
        fileErrors.Clear();
        VerifyDiscoveryCopiesAndGuards(discovery, discoveryWithReplacementFiles);
        VerifyManagementResultCopies(valid, invalid, normalize, loadFailed, loadValidated, reloadFailed);
        VerifyManagementResultGuards(discovery);
        VerifyNormalizeResponseCopies(normalize);
        VerifyValidationResponseCopies(invalid);
        VerifyReloadResponseCopies(apiReloadFailed);
        VerifyDiscoveryResponseCopies(discovery);
    }

    private static void VerifyManagementResultGuards(ProxyConfigurationDiscovery discovery)
    {
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationNormalizeResult.Failed("json", [null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationValidationResult.Valid(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, lastSuccessfulLoadAtUtc: DateTimeOffset.UnixEpoch, wouldBeVersion: 2, [null!], discovery));
        AssertEx.Throws<ArgumentException>(() => ProxyConfigurationValidationResult.Valid(sourceDirectory: " ", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, lastSuccessfulLoadAtUtc: DateTimeOffset.UnixEpoch, wouldBeVersion: 2, ["sites/home.json"], discovery));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationValidationResult.Valid(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, lastSuccessfulLoadAtUtc: DateTimeOffset.UnixEpoch, wouldBeVersion: 2, ["sites/home.json"], discovery: null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationValidationResult.Invalid(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, lastSuccessfulLoadAtUtc: DateTimeOffset.UnixEpoch, wouldBeVersion: null, ["sites/home.json"], discovery, [null!], []));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationValidationResult.Invalid(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, lastSuccessfulLoadAtUtc: DateTimeOffset.UnixEpoch, wouldBeVersion: null, ["sites/home.json"], discovery, [], [null!]));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationLoadResult.FailedResult(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, sourceFiles: [null!], discovery, fileErrors: [ProxyConfigurationFileError.ForPath("sites/home.json", "parse failed")], wouldBeVersion: null));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationLoadResult.FailedResult(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, sourceFiles: ["sites/home.json"], discovery, fileErrors: [null!], wouldBeVersion: null));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationLoadResult.ValidatedResult(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, sourceFiles: [null!], discovery, wouldBeVersion: 2));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationReloadResult<TestConfigurationProjection>.LoadFailed(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, loadedAtUtc: DateTimeOffset.UnixEpoch, discovery, [null!], [], activeConfiguration: null));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationReloadResult<TestConfigurationProjection>.LoadFailed(sourceDirectory: "data", attemptedAtUtc: DateTimeOffset.UnixEpoch, activeVersion: 1, loadedAtUtc: DateTimeOffset.UnixEpoch, discovery, [], [null!], activeConfiguration: null));
    }

    private static void VerifyManagementResultCopies(ProxyConfigurationValidationResult valid, ProxyConfigurationValidationResult invalid, ProxyConfigurationNormalizeResult normalize, ProxyConfigurationLoadResult.FailedResult loadFailed, ProxyConfigurationLoadResult.ValidatedResult loadValidated, ProxyConfigurationReloadResult<TestConfigurationProjection> reloadFailed)
    {
        AssertEx.Equal("sites/home.json", valid.SourceFiles[0]);
        AssertEx.False(valid.SourceFiles is string[], "Validation source files should not expose a mutable array.");
        AssertEx.Equal("sites/home.json", invalid.SourceFiles[0]);
        AssertEx.Equal("parse failed", invalid.Errors[0]);
        AssertEx.Equal("sites/home.json", invalid.FileErrors[0].Path);
        AssertEx.False(invalid.SourceFiles is string[], "Invalid validation source files should not expose a mutable array.");
        AssertEx.False(invalid.Errors is string[], "Invalid validation errors should not expose a mutable array.");
        AssertEx.False(invalid.FileErrors is ProxyConfigurationFileError[], "Invalid validation file errors should not expose a mutable array.");
        AssertEx.Equal("sites/home.json: parse failed", normalize.Errors[0]);
        AssertEx.Equal("sites/home.json", normalize.FileErrors[0].Path);
        AssertEx.False(normalize.Errors is string[], "Normalize errors should not expose a mutable array.");
        AssertEx.False(normalize.FileErrors is ProxyConfigurationFileError[], "Normalize file errors should not expose a mutable array.");
        AssertEx.Equal("sites/home.json", loadFailed.SourceFiles[0]);
        AssertEx.Equal("sites/home.json", loadFailed.FileErrors[0].Path);
        AssertEx.Equal("sites/home.json: parse failed", loadFailed.Errors[0]);
        AssertEx.False(loadFailed.SourceFiles is string[], "Failed load source files should not expose a mutable array.");
        AssertEx.False(loadFailed.Errors is string[], "Failed load errors should not expose a mutable array.");
        AssertEx.False(loadFailed.FileErrors is ProxyConfigurationFileError[], "Failed load file errors should not expose a mutable array.");
        AssertEx.Equal("sites/home.json", loadValidated.SourceFiles[0]);
        AssertEx.False(loadValidated.SourceFiles is string[], "Validated load source files should not expose a mutable array.");
        AssertEx.Equal("parse failed", reloadFailed.Errors[0]);
        AssertEx.Equal("sites/home.json", reloadFailed.FileErrors[0].Path);
        AssertEx.False(reloadFailed.Errors is string[], "Reload errors should not expose a mutable array.");
        AssertEx.False(reloadFailed.FileErrors is ProxyConfigurationFileError[], "Reload file errors should not expose a mutable array.");
    }

    private static void VerifyDiscoveryCopiesAndGuards(ProxyConfigurationDiscovery discovery, ProxyConfigurationDiscovery discoveryWithReplacementFiles)
    {
        AssertEx.Equal("sites/home.json", discovery.Files[0].Path);
        AssertEx.Equal("tests/config", discovery.CreatedPaths[0]);
        AssertEx.Equal("tests/config/sites", discovery.ExistingPaths[0]);
        AssertEx.False(discovery.Files is ProxyConfigurationFileDiscovery[], "Discovery files should not expose a mutable array.");
        AssertEx.False(discovery.CreatedPaths is string[], "Discovery created paths should not expose a mutable array.");
        AssertEx.False(discovery.ExistingPaths is string[], "Discovery existing paths should not expose a mutable array.");
        AssertEx.Equal("sites/replacement.json", discoveryWithReplacementFiles.Files[0].Path);
        AssertEx.Equal("tests/config", discoveryWithReplacementFiles.CreatedPaths[0]);
        AssertEx.Equal("tests/config/sites", discoveryWithReplacementFiles.ExistingPaths[0]);
        AssertEx.False(discoveryWithReplacementFiles.Files is ProxyConfigurationFileDiscovery[], "Discovery replacement files should not expose a mutable array.");
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigurationFileDiscovery(" ", "json", "loaded", null));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigurationFileDiscovery("sites/home.json", " ", "loaded", null));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigurationFileDiscovery("sites/home.json", "json", " ", null));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigurationFileDiscovery("sites/home.json", "json", "loaded", " "));
        AssertEx.Throws<ArgumentException>(() => new ProxyFilesystemLayout(" ", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"));
        AssertEx.Throws<ArgumentException>(() => new ProxyFilesystemLayout("tests", " ", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"));
        AssertEx.Throws<ArgumentException>(() => new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", " "));
    }

    private static void VerifyNormalizeResponseCopies(ProxyConfigurationNormalizeResult normalize)
    {
        var normalizeResponse = ProxyConfigurationNormalizeResponseMapper.FromResult(normalize);
        AssertEx.False(normalizeResponse.Errors is string[], "Normalize API errors should not expose a mutable array.");
        AssertEx.False(normalizeResponse.FileErrors is ProxyConfigurationFileErrorResponse[], "Normalize API file errors should not expose a mutable array.");
        var normalizeResponseErrors = new List<string>
        {
            normalizeResponse.Errors[0]
        };
        var normalizeResponseFileErrors = new List<ProxyConfigurationFileErrorResponse>
        {
            normalizeResponse.FileErrors[0]
        };
        var directNormalizeResponse = new ProxyConfigurationNormalizeResponse(succeeded: false, format: "json", canonicalJson: null, errors: normalizeResponseErrors, fileErrors: normalizeResponseFileErrors);
        normalizeResponseErrors[0] = "replacement error";
        normalizeResponseFileErrors[0] = normalizeResponseFileErrors[0] with
        {
            Path = "replacement.json"
        };
        normalizeResponseErrors.Clear();
        normalizeResponseFileErrors.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationNormalizeResponse(succeeded: false, format: "json", canonicalJson: null, errors: null!, fileErrors: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationNormalizeResponse(succeeded: false, format: "json", canonicalJson: null, errors: [], fileErrors: null!));
        AssertEx.Equal("sites/home.json: parse failed", directNormalizeResponse.Errors[0]);
        AssertEx.Equal("sites/home.json", directNormalizeResponse.FileErrors[0].Path);
        AssertEx.False(directNormalizeResponse.Errors is string[], "Direct normalize API errors should not expose a mutable array.");
        AssertEx.False(directNormalizeResponse.FileErrors is ProxyConfigurationFileErrorResponse[], "Direct normalize API file errors should not expose a mutable array.");
    }

    private static void VerifyValidationResponseCopies(ProxyConfigurationValidationResult invalid)
    {
        var validationResponse = ProxyConfigurationValidationResponseMapper.FromResult(invalid);
        AssertEx.False(validationResponse.SourceFiles is string[], "Validation API source files should not expose a mutable array.");
        AssertEx.False(validationResponse.Errors is string[], "Validation API errors should not expose a mutable array.");
        AssertEx.False(validationResponse.FileErrors is ProxyConfigurationFileErrorResponse[], "Validation API file errors should not expose a mutable array.");
        var validationResponseSourceFiles = new List<string>
        {
            validationResponse.SourceFiles[0]
        };
        var validationResponseErrors = new List<string>
        {
            validationResponse.Errors[0]
        };
        var validationResponseFileErrors = new List<ProxyConfigurationFileErrorResponse>
        {
            validationResponse.FileErrors[0]
        };
        var directValidationResponse = new ProxyConfigurationValidationResponse(succeeded: false, sourceDirectory: validationResponse.SourceDirectory, attemptedAtUtc: validationResponse.AttemptedAtUtc, activeVersion: validationResponse.ActiveVersion, lastSuccessfulLoadAtUtc: validationResponse.LastSuccessfulLoadAtUtc, wouldBeVersion: validationResponse.WouldBeVersion, sourceFiles: validationResponseSourceFiles, discovery: validationResponse.Discovery, errors: validationResponseErrors, fileErrors: validationResponseFileErrors);
        validationResponseSourceFiles[0] = "sites/replacement.json";
        validationResponseErrors[0] = "replacement error";
        validationResponseFileErrors[0] = validationResponseFileErrors[0] with
        {
            Path = "sites/replacement.json"
        };
        validationResponseSourceFiles.Clear();
        validationResponseErrors.Clear();
        validationResponseFileErrors.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationValidationResponse(succeeded: false, sourceDirectory: validationResponse.SourceDirectory, attemptedAtUtc: validationResponse.AttemptedAtUtc, activeVersion: validationResponse.ActiveVersion, lastSuccessfulLoadAtUtc: validationResponse.LastSuccessfulLoadAtUtc, wouldBeVersion: validationResponse.WouldBeVersion, sourceFiles: null!, discovery: validationResponse.Discovery, errors: [], fileErrors: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationValidationResponse(succeeded: false, sourceDirectory: validationResponse.SourceDirectory, attemptedAtUtc: validationResponse.AttemptedAtUtc, activeVersion: validationResponse.ActiveVersion, lastSuccessfulLoadAtUtc: validationResponse.LastSuccessfulLoadAtUtc, wouldBeVersion: validationResponse.WouldBeVersion, sourceFiles: [], discovery: null!, errors: [], fileErrors: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationValidationResponse(succeeded: false, sourceDirectory: validationResponse.SourceDirectory, attemptedAtUtc: validationResponse.AttemptedAtUtc, activeVersion: validationResponse.ActiveVersion, lastSuccessfulLoadAtUtc: validationResponse.LastSuccessfulLoadAtUtc, wouldBeVersion: validationResponse.WouldBeVersion, sourceFiles: [], discovery: validationResponse.Discovery, errors: null!, fileErrors: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationValidationResponse(succeeded: false, sourceDirectory: validationResponse.SourceDirectory, attemptedAtUtc: validationResponse.AttemptedAtUtc, activeVersion: validationResponse.ActiveVersion, lastSuccessfulLoadAtUtc: validationResponse.LastSuccessfulLoadAtUtc, wouldBeVersion: validationResponse.WouldBeVersion, sourceFiles: [], discovery: validationResponse.Discovery, errors: [], fileErrors: null!));
        AssertEx.Equal("sites/home.json", directValidationResponse.SourceFiles[0]);
        AssertEx.Equal("parse failed", directValidationResponse.Errors[0]);
        AssertEx.Equal("sites/home.json", directValidationResponse.FileErrors[0].Path);
        AssertEx.False(directValidationResponse.SourceFiles is string[], "Direct validation API source files should not expose a mutable array.");
        AssertEx.False(directValidationResponse.Errors is string[], "Direct validation API errors should not expose a mutable array.");
        AssertEx.False(directValidationResponse.FileErrors is ProxyConfigurationFileErrorResponse[], "Direct validation API file errors should not expose a mutable array.");
    }

    private static void VerifyReloadResponseCopies(ProxyConfigurationReloadResult<ProxyConfigurationProjection> apiReloadFailed)
    {
        var reloadResponse = ProxyConfigurationReloadResponseMapper.FromResult(apiReloadFailed);
        AssertEx.False(reloadResponse.Errors is string[], "Reload API errors should not expose a mutable array.");
        AssertEx.False(reloadResponse.FileErrors is ProxyConfigurationFileErrorResponse[], "Reload API file errors should not expose a mutable array.");
        var reloadResponseErrors = new List<string>
        {
            reloadResponse.Errors[0]
        };
        var reloadResponseFileErrors = new List<ProxyConfigurationFileErrorResponse>
        {
            reloadResponse.FileErrors[0]
        };
        var directReloadResponse = new ProxyConfigurationReloadResponse(succeeded: false, sourceDirectory: reloadResponse.SourceDirectory, attemptedAtUtc: reloadResponse.AttemptedAtUtc, activeVersion: reloadResponse.ActiveVersion, loadedAtUtc: reloadResponse.LoadedAtUtc, lastSuccessfulLoadAtUtc: reloadResponse.LastSuccessfulLoadAtUtc, discovery: reloadResponse.Discovery, errors: reloadResponseErrors, fileErrors: reloadResponseFileErrors, activeConfiguration: reloadResponse.ActiveConfiguration, listenerReload: reloadResponse.ListenerReload);
        reloadResponseErrors[0] = "replacement error";
        reloadResponseFileErrors[0] = reloadResponseFileErrors[0] with
        {
            Path = "sites/replacement.json"
        };
        reloadResponseErrors.Clear();
        reloadResponseFileErrors.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationReloadResponse(succeeded: false, sourceDirectory: reloadResponse.SourceDirectory, attemptedAtUtc: reloadResponse.AttemptedAtUtc, activeVersion: reloadResponse.ActiveVersion, loadedAtUtc: reloadResponse.LoadedAtUtc, lastSuccessfulLoadAtUtc: reloadResponse.LastSuccessfulLoadAtUtc, discovery: null!, errors: [], fileErrors: [], activeConfiguration: null, listenerReload: null));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationReloadResponse(succeeded: false, sourceDirectory: reloadResponse.SourceDirectory, attemptedAtUtc: reloadResponse.AttemptedAtUtc, activeVersion: reloadResponse.ActiveVersion, loadedAtUtc: reloadResponse.LoadedAtUtc, lastSuccessfulLoadAtUtc: reloadResponse.LastSuccessfulLoadAtUtc, discovery: reloadResponse.Discovery, errors: null!, fileErrors: [], activeConfiguration: null, listenerReload: null));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationReloadResponse(succeeded: false, sourceDirectory: reloadResponse.SourceDirectory, attemptedAtUtc: reloadResponse.AttemptedAtUtc, activeVersion: reloadResponse.ActiveVersion, loadedAtUtc: reloadResponse.LoadedAtUtc, lastSuccessfulLoadAtUtc: reloadResponse.LastSuccessfulLoadAtUtc, discovery: reloadResponse.Discovery, errors: [], fileErrors: null!, activeConfiguration: null, listenerReload: null));
        AssertEx.Equal("parse failed", directReloadResponse.Errors[0]);
        AssertEx.Equal("sites/home.json", directReloadResponse.FileErrors[0].Path);
        AssertEx.False(directReloadResponse.Errors is string[], "Direct reload API errors should not expose a mutable array.");
        AssertEx.False(directReloadResponse.FileErrors is ProxyConfigurationFileErrorResponse[], "Direct reload API file errors should not expose a mutable array.");
    }

    private static void VerifyDiscoveryResponseCopies(ProxyConfigurationDiscovery discovery)
    {
        var discoveryResponse = ProxyConfigurationDiscoveryResponseMapper.FromDiscovery(discovery);
        AssertEx.False(discoveryResponse.Files is ProxyConfigurationFileDiscoveryResponse[], "Discovery API files should not expose a mutable array.");
        AssertEx.False(discoveryResponse.CreatedPaths is string[], "Discovery API created paths should not expose a mutable array.");
        AssertEx.False(discoveryResponse.ExistingPaths is string[], "Discovery API existing paths should not expose a mutable array.");
        var discoveryResponseFiles = new List<ProxyConfigurationFileDiscoveryResponse>
        {
            discoveryResponse.Files[0]
        };
        var discoveryResponseCreatedPaths = new List<string>
        {
            discoveryResponse.CreatedPaths[0]
        };
        var discoveryResponseExistingPaths = new List<string>
        {
            discoveryResponse.ExistingPaths[0]
        };
        var directDiscoveryResponse = new ProxyConfigurationDiscoveryResponse(layout: discoveryResponse.Layout, files: discoveryResponseFiles, createdPaths: discoveryResponseCreatedPaths, existingPaths: discoveryResponseExistingPaths);
        discoveryResponseFiles[0] = discoveryResponseFiles[0] with
        {
            Path = "sites/replacement.json"
        };
        discoveryResponseCreatedPaths[0] = "tests/replacement-created";
        discoveryResponseExistingPaths[0] = "tests/replacement-existing";
        discoveryResponseFiles.Clear();
        discoveryResponseCreatedPaths.Clear();
        discoveryResponseExistingPaths.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationDiscoveryResponse(layout: null!, files: [], createdPaths: [], existingPaths: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationDiscoveryResponse(layout: discoveryResponse.Layout, files: null!, createdPaths: [], existingPaths: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationDiscoveryResponse(layout: discoveryResponse.Layout, files: [], createdPaths: null!, existingPaths: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationDiscoveryResponse(layout: discoveryResponse.Layout, files: [], createdPaths: [], existingPaths: null!));
        AssertEx.Equal("sites/home.json", directDiscoveryResponse.Files[0].Path);
        AssertEx.Equal("tests/config", directDiscoveryResponse.CreatedPaths[0]);
        AssertEx.Equal("tests/config/sites", directDiscoveryResponse.ExistingPaths[0]);
        AssertEx.False(directDiscoveryResponse.Files is ProxyConfigurationFileDiscoveryResponse[], "Direct discovery API files should not expose a mutable array.");
        AssertEx.False(directDiscoveryResponse.CreatedPaths is string[], "Direct discovery API created paths should not expose a mutable array.");
        AssertEx.False(directDiscoveryResponse.ExistingPaths is string[], "Direct discovery API existing paths should not expose a mutable array.");
    }

    public static void RuntimeConfigurationPolicyRecordsCopyInputCollections()
    {
        var acmeDomains = new List<string>
        {
            "home.test"
        };
        var acmeContacts = new List<string>
        {
            "admin@home.test"
        };
        var acmeCertificates = new List<RuntimeAcmeCertificateOptions>
        {
            new("home-cert", true, acmeDomains, 21)
        };
        var acmeProjectionDomains = new List<string>
        {
            "api.home.test"
        };
        var acmeProjectionContacts = new List<string>
        {
            "ops@home.test"
        };
        var acmeProjectionCertificates = new List<RuntimeAcmeCertificateProjection>
        {
            new("api-cert", true, acmeProjectionDomains, 14)
        };
        var adminUrls = new List<string>
        {
            "http://127.0.0.1:18081"
        };
        var projectionUrls = new List<string>
        {
            "http://127.0.0.1:18082"
        };
        var trustedProxies = new List<string>
        {
            "127.0.0.1"
        };
        var cacheVaryHeaders = new List<string>
        {
            "X-Tenant"
        };
        var cacheStatusCodes = new List<int>
        {
            200
        };
        var cacheMethods = new List<string>
        {
            "GET"
        };
        var certificateDomains = new List<string>
        {
            "home.test"
        };
        var projectionDomains = new List<string>
        {
            "api.home.test"
        };
        var circuitBreakerCodes = new List<int>
        {
            503
        };
        var setRequestHeaders = new List<ProxyHeaderField>
        {
            new("X-Trace", "enabled")
        };
        var removeRequestHeaders = new List<string>
        {
            "X-Remove-Request"
        };
        var setResponseHeaders = new List<ProxyHeaderField>
        {
            new("X-Frame-Options", "DENY")
        };
        var removeResponseHeaders = new List<string>
        {
            "Server"
        };
        var setRequestHeaderProjections = new List<RuntimeHeaderFieldProjection>
        {
            new("X-Trace", "enabled")
        };
        var setResponseHeaderProjections = new List<RuntimeHeaderFieldProjection>
        {
            new("X-Frame-Options", "DENY")
        };
        var retryStatusCodes = new List<int>
        {
            502
        };
        var retryMethods = new List<string>
        {
            "GET"
        };
        using var certificate = X509CertificateLoader.LoadPkcs12(TestCertificates.CreateSelfSignedPfxBytes("home.test"), password: null);
        var acme = new RuntimeAcmeOptions(true, false, "https://acme.test/directory", acmeContacts, true, "acme", 21, 60, 10, acmeCertificates);
        var acmeProjection = new RuntimeAcmeProjection(true, false, "https://acme.test/directory", acmeProjectionContacts, true, "acme", 14, 60, 10, acmeProjectionCertificates);
        var admin = new RuntimeAdminSecurityOptions(adminUrls, RequireAuthentication: true, HasConfiguredToken: true, Token: "secret", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: "environment", RecentAuditCapacity: 128);
        var adminProjection = new RuntimeAdminSecurityProjection(projectionUrls, RequireAuthentication: true, HasConfiguredToken: true, Token: "***", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: "environment", RecentAuditCapacity: 128);
        var forwardedHeaders = new RuntimeForwardedHeadersOptions(Enabled: true, TrustedProxies: trustedProxies);
        var forwardedHeadersProjection = new RuntimeForwardedHeadersProjection(Enabled: true, TrustedProxies: trustedProxies);
        var cache = new RuntimeCachePolicy(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: cacheVaryHeaders.Select(static header => header), CacheableStatusCodes: cacheStatusCodes.Select(static statusCode => statusCode), Methods: cacheMethods.Select(static method => method));
        var cacheProjection = new RuntimeCacheProjection(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: cacheVaryHeaders.Select(static header => header), CacheableStatusCodes: cacheStatusCodes.Select(static statusCode => statusCode), Methods: cacheMethods.Select(static method => method));
        var runtimeCertificate = new RuntimeCertificate("home-cert", "certs/home.pfx", "pfx", HasConfiguredPassword: false, certificate, "manual", certificateDomains);
        var certificateProjection = new RuntimeCertificateProjection("api-cert", "certs/api.pfx", "pfx", "manual", projectionDomains, HasConfiguredPassword: false, Subject: "CN=api.home.test", Thumbprint: "thumbprint", NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30));
        var circuitBreaker = new RuntimeCircuitBreakerPolicy(Enabled: true, FailureThreshold: 5, SamplingWindow: TimeSpan.FromSeconds(60), OpenDuration: TimeSpan.FromSeconds(30), HalfOpenMaxAttempts: 1, FailureStatusCodes: circuitBreakerCodes);
        var headerPolicy = new RuntimeHeaderPolicy(setRequestHeaders.Select(static header => header), removeRequestHeaders.Select(static header => header), setResponseHeaders.Select(static header => header), removeResponseHeaders.Select(static header => header));
        var headerPolicyProjection = new RuntimeHeaderPolicyProjection(setRequestHeaderProjections.Select(static header => header), removeRequestHeaders.Select(static header => header), setResponseHeaderProjections.Select(static header => header), removeResponseHeaders.Select(static header => header));
        var retry = new RuntimeRetryPolicy(Enabled: true, MaxAttempts: 2, PerAttemptTimeout: TimeSpan.FromSeconds(1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: retryStatusCodes.Select(static statusCode => statusCode), RetryMethods: retryMethods.Select(static method => method), RetryBackoff: TimeSpan.FromMilliseconds(50));
        var retryProjection = new RuntimeRetryProjection(Enabled: true, MaxAttempts: 2, PerAttemptTimeout: TimeSpan.FromSeconds(1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: retryStatusCodes.Select(static statusCode => statusCode), RetryMethods: retryMethods.Select(static method => method), RetryBackoff: TimeSpan.FromMilliseconds(50));
        acmeDomains.Clear();
        acmeContacts.Clear();
        acmeCertificates.Clear();
        acmeProjectionDomains.Clear();
        acmeProjectionContacts.Clear();
        acmeProjectionCertificates.Clear();
        adminUrls.Clear();
        projectionUrls.Clear();
        trustedProxies.Clear();
        cacheVaryHeaders.Clear();
        cacheStatusCodes.Clear();
        cacheMethods.Clear();
        certificateDomains.Clear();
        projectionDomains.Clear();
        circuitBreakerCodes.Clear();
        setRequestHeaders.Clear();
        removeRequestHeaders.Clear();
        setResponseHeaders.Clear();
        removeResponseHeaders.Clear();
        setRequestHeaderProjections.Clear();
        setResponseHeaderProjections.Clear();
        retryStatusCodes.Clear();
        retryMethods.Clear();
        AssertEx.Equal("home.test", acme.Certificates[0].Domains[0]);
        AssertEx.Equal("admin@home.test", acme.ContactEmails[0]);
        AssertEx.Equal("api.home.test", acmeProjection.Certificates[0].Domains[0]);
        AssertEx.Equal("ops@home.test", acmeProjection.ContactEmails[0]);
        AssertEx.Equal("api-cert", acmeProjection.Certificates[0].Id);
        AssertEx.Equal(14, acmeProjection.Certificates[0].RenewBeforeDays);
        AssertEx.Equal("http://127.0.0.1:18081", admin.Urls[0]);
        AssertEx.Equal("http://127.0.0.1:18082", adminProjection.Urls[0]);
        AssertEx.Equal("***", adminProjection.Token);
        AssertEx.Equal("environment", adminProjection.TokenSource);
        AssertEx.Equal(128, adminProjection.RecentAuditCapacity);
        AssertEx.Equal("127.0.0.1", forwardedHeaders.TrustedProxies[0]);
        AssertEx.Equal("127.0.0.1", forwardedHeadersProjection.TrustedProxies[0]);
        AssertEx.True(forwardedHeadersProjection.Enabled);
        AssertEx.Equal("X-Tenant", cache.VaryByHeaders[0]);
        AssertEx.Equal(200, cache.CacheableStatusCodes[0]);
        AssertEx.Equal("GET", cache.Methods[0]);
        AssertEx.Equal("X-Tenant", cacheProjection.VaryByHeaders[0]);
        AssertEx.Equal(200, cacheProjection.CacheableStatusCodes[0]);
        AssertEx.Equal("GET", cacheProjection.Methods[0]);
        AssertEx.True(cacheProjection.Enabled);
        AssertEx.Equal(1024L, cacheProjection.MaxEntryBytes);
        AssertEx.Equal(TimeSpan.FromSeconds(60), cacheProjection.DefaultTtl);
        AssertEx.Equal("home.test", runtimeCertificate.Domains[0]);
        AssertEx.Equal("api.home.test", certificateProjection.Domains[0]);
        AssertEx.Equal("api-cert", certificateProjection.Id);
        AssertEx.Equal("manual", certificateProjection.Source);
        AssertEx.Equal(DateTime.UnixEpoch.AddDays(30), certificateProjection.NotAfter);
        AssertEx.Equal(503, circuitBreaker.FailureStatusCodes[0]);
        AssertEx.Equal("X-Trace", headerPolicy.SetRequestHeaders[0].Name);
        AssertEx.Equal("X-Remove-Request", headerPolicy.RemoveRequestHeaders[0]);
        AssertEx.Equal("X-Frame-Options", headerPolicy.SetResponseHeaders[0].Name);
        AssertEx.Equal("Server", headerPolicy.RemoveResponseHeaders[0]);
        AssertEx.Equal("X-Trace", headerPolicyProjection.SetRequestHeaders[0].Name);
        AssertEx.Equal("X-Remove-Request", headerPolicyProjection.RemoveRequestHeaders[0]);
        AssertEx.Equal("X-Frame-Options", headerPolicyProjection.SetResponseHeaders[0].Name);
        AssertEx.Equal("Server", headerPolicyProjection.RemoveResponseHeaders[0]);
        AssertEx.Equal(502, retry.RetryOnStatusCodes[0]);
        AssertEx.Equal("GET", retry.RetryMethods[0]);
        AssertEx.Equal(502, retryProjection.RetryOnStatusCodes[0]);
        AssertEx.Equal("GET", retryProjection.RetryMethods[0]);
        AssertEx.True(retryProjection.Enabled);
        AssertEx.Equal(2, retryProjection.MaxAttempts);
        AssertEx.Equal(TimeSpan.FromMilliseconds(50), retryProjection.RetryBackoff);
        AssertEx.False(acme.ContactEmails is string[]);
        AssertEx.False(acme.Certificates is RuntimeAcmeCertificateOptions[]);
        AssertEx.False(acme.Certificates[0].Domains is string[]);
        AssertEx.False(acmeProjection.ContactEmails is string[]);
        AssertEx.False(acmeProjection.Certificates is RuntimeAcmeCertificateProjection[]);
        AssertEx.False(acmeProjection.Certificates[0].Domains is string[]);
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAcmeProjection(true, false, null!, [], true, "acme", 14, 60, 10, []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAcmeProjection(true, false, "https://acme.test/directory", [], true, null!, 14, 60, 10, []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAcmeProjection(true, false, "https://acme.test/directory", null!, true, "acme", 14, 60, 10, []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAcmeProjection(true, false, "https://acme.test/directory", [], true, "acme", 14, 60, 10, null!));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAcmeCertificateProjection(null!, true, [], 14));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAcmeCertificateProjection("api-cert", true, null!, 14));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificateProjection(null!, "certs/api.pfx", "pfx", "manual", [], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificateProjection("api-cert", null!, "pfx", "manual", [], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificateProjection("api-cert", "certs/api.pfx", null!, "manual", [], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificateProjection("api-cert", "certs/api.pfx", "pfx", null!, [], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificateProjection("api-cert", "certs/api.pfx", "pfx", "manual", null!, HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentException>(() => new RuntimeCertificateProjection(" ", "certs/api.pfx", "pfx", "manual", [], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentException>(() => new RuntimeCertificateProjection("api-cert", "", "pfx", "manual", [], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentException>(() => new RuntimeCertificateProjection("api-cert", "certs/api.pfx", "\t", "manual", [], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentException>(() => new RuntimeCertificateProjection("api-cert", "certs/api.pfx", "pfx", " ", [], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificateProjection("api-cert", "certs/api.pfx", "pfx", "manual", [null!], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch, NotAfter: DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCertificateProjection("api-cert", "certs/api.pfx", "pfx", "manual", [], HasConfiguredPassword: false, Subject: null, Thumbprint: null, NotBefore: DateTime.UnixEpoch.AddDays(30), NotAfter: DateTime.UnixEpoch));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAdminSecurityProjection(Urls: null!, RequireAuthentication: true, HasConfiguredToken: true, Token: "***", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: "environment", RecentAuditCapacity: 128));
        AssertEx.Throws<ArgumentException>(() => new RuntimeAdminSecurityOptions(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "secret", TokenEnvironmentVariable: " ", TokenSource: "environment", RecentAuditCapacity: 128));
        AssertEx.Throws<ArgumentException>(() => new RuntimeAdminSecurityOptions(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "secret", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: " ", RecentAuditCapacity: 128));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeAdminSecurityOptions(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "secret", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: "environment", RecentAuditCapacity: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeAdminSecurityOptions(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "secret", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: "environment", RecentAuditCapacity: 10_001));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAdminSecurityProjection(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "***", TokenEnvironmentVariable: null!, TokenSource: "environment", RecentAuditCapacity: 128));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAdminSecurityProjection(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "***", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: null!, RecentAuditCapacity: 128));
        AssertEx.Throws<ArgumentException>(() => new RuntimeAdminSecurityProjection(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "***", TokenEnvironmentVariable: " ", TokenSource: "environment", RecentAuditCapacity: 128));
        AssertEx.Throws<ArgumentException>(() => new RuntimeAdminSecurityProjection(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "***", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: " ", RecentAuditCapacity: 128));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeAdminSecurityProjection(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "***", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: "environment", RecentAuditCapacity: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeAdminSecurityProjection(Urls: [], RequireAuthentication: true, HasConfiguredToken: true, Token: "***", TokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", TokenSource: "environment", RecentAuditCapacity: 10_001));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeForwardedHeadersProjection(Enabled: true, TrustedProxies: null!));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCacheProjection(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: null!, CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCacheProjection(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: null!, Methods: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCacheProjection(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: null!));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCacheProjection(Enabled: true, MaxEntryBytes: -1, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCacheProjection(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: -1, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCacheProjection(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromTicks(-1), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRetryProjection(Enabled: true, MaxAttempts: 2, PerAttemptTimeout: TimeSpan.FromSeconds(1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: null!, RetryMethods: [], RetryBackoff: TimeSpan.FromMilliseconds(50)));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRetryProjection(Enabled: true, MaxAttempts: 2, PerAttemptTimeout: TimeSpan.FromSeconds(1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: [], RetryMethods: null!, RetryBackoff: TimeSpan.FromMilliseconds(50)));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeRetryProjection(Enabled: true, MaxAttempts: 0, PerAttemptTimeout: TimeSpan.FromSeconds(1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: [], RetryMethods: [], RetryBackoff: TimeSpan.FromMilliseconds(50)));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeRetryProjection(Enabled: true, MaxAttempts: 2, PerAttemptTimeout: TimeSpan.FromTicks(-1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: [], RetryMethods: [], RetryBackoff: TimeSpan.FromMilliseconds(50)));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeRetryProjection(Enabled: true, MaxAttempts: 2, PerAttemptTimeout: TimeSpan.FromSeconds(1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: [], RetryMethods: [], RetryBackoff: TimeSpan.FromTicks(-1)));
        AssertEx.False(admin.Urls is string[]);
        AssertEx.False(adminProjection.Urls is string[]);
        AssertEx.False(forwardedHeaders.TrustedProxies is string[]);
        AssertEx.False(forwardedHeadersProjection.TrustedProxies is string[]);
        AssertEx.False(cache.VaryByHeaders is string[]);
        AssertEx.False(cache.CacheableStatusCodes is int[]);
        AssertEx.False(cache.Methods is string[]);
        AssertEx.False(cacheProjection.VaryByHeaders is string[]);
        AssertEx.False(cacheProjection.CacheableStatusCodes is int[]);
        AssertEx.False(cacheProjection.Methods is string[]);
        AssertEx.False(runtimeCertificate.Domains is string[]);
        AssertEx.False(certificateProjection.Domains is string[]);
        AssertEx.False(circuitBreaker.FailureStatusCodes is int[]);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCircuitBreakerPolicy(Enabled: true, FailureThreshold: 0, SamplingWindow: TimeSpan.FromSeconds(30), OpenDuration: TimeSpan.FromSeconds(10), HalfOpenMaxAttempts: 1, FailureStatusCodes: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCircuitBreakerPolicy(Enabled: true, FailureThreshold: 2, SamplingWindow: TimeSpan.Zero, OpenDuration: TimeSpan.FromSeconds(10), HalfOpenMaxAttempts: 1, FailureStatusCodes: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCircuitBreakerPolicy(Enabled: true, FailureThreshold: 2, SamplingWindow: TimeSpan.FromSeconds(30), OpenDuration: TimeSpan.Zero, HalfOpenMaxAttempts: 1, FailureStatusCodes: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCircuitBreakerPolicy(Enabled: true, FailureThreshold: 2, SamplingWindow: TimeSpan.FromSeconds(30), OpenDuration: TimeSpan.FromSeconds(10), HalfOpenMaxAttempts: 0, FailureStatusCodes: []));
        AssertEx.False(headerPolicy.SetRequestHeaders is ProxyHeaderField[]);
        AssertEx.False(headerPolicy.RemoveRequestHeaders is string[]);
        AssertEx.False(headerPolicy.SetResponseHeaders is ProxyHeaderField[]);
        AssertEx.False(headerPolicy.RemoveResponseHeaders is string[]);
        AssertEx.False(headerPolicyProjection.SetRequestHeaders is RuntimeHeaderFieldProjection[]);
        AssertEx.False(headerPolicyProjection.RemoveRequestHeaders is string[]);
        AssertEx.False(headerPolicyProjection.SetResponseHeaders is RuntimeHeaderFieldProjection[]);
        AssertEx.False(headerPolicyProjection.RemoveResponseHeaders is string[]);
        AssertEx.False(retry.RetryOnStatusCodes is int[]);
        AssertEx.False(retry.RetryMethods is string[]);
        AssertEx.False(retryProjection.RetryOnStatusCodes is int[]);
        AssertEx.False(retryProjection.RetryMethods is string[]);
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCachePolicy(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [null!], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCachePolicy(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: [null!]));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCachePolicy(Enabled: true, MaxEntryBytes: -1, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCachePolicy(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: -1, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCachePolicy(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromTicks(-1), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHeaderPolicy([null!], [], [], []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHeaderPolicy([], [], [null!], []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderPolicy([new ProxyHeaderField(" ", "value")], [], [], []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderPolicy([new ProxyHeaderField("Host", "value")], [], [], []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderPolicy([new ProxyHeaderField("Bad Header", "value")], [], [], []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderPolicy([new ProxyHeaderField("X-Test", "bad\r\nvalue")], [], [], []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderPolicy([], ["Host"], [], []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderPolicy([], ["Bad Header"], [], []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderFieldProjection(" ", "value"));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderFieldProjection("Host", "value"));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderFieldProjection("Bad Header", "value"));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHeaderFieldProjection("X-Test", null!));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderFieldProjection("X-Test", "bad\nvalue"));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHeaderPolicyProjection([null!], [], [], []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderPolicyProjection([], ["Host"], [], []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHeaderPolicyProjection([], ["Bad Header"], [], []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRetryPolicy(Enabled: true, MaxAttempts: 2, PerAttemptTimeout: TimeSpan.FromSeconds(1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: [], RetryMethods: [null!], RetryBackoff: TimeSpan.FromMilliseconds(50)));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeRetryPolicy(Enabled: true, MaxAttempts: 0, PerAttemptTimeout: TimeSpan.FromSeconds(1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: [], RetryMethods: [], RetryBackoff: TimeSpan.FromMilliseconds(50)));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeRetryPolicy(Enabled: true, MaxAttempts: 2, PerAttemptTimeout: TimeSpan.FromTicks(-1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: [], RetryMethods: [], RetryBackoff: TimeSpan.FromMilliseconds(50)));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeRetryPolicy(Enabled: true, MaxAttempts: 2, PerAttemptTimeout: TimeSpan.FromSeconds(1), RetryOnConnectFailure: true, RetryOnUpstreamResponseHeadTimeout: true, RetryOnStatusCodes: [], RetryMethods: [], RetryBackoff: TimeSpan.FromTicks(-1)));
        var adminResponse = RuntimeAdminSecurityResponseMapper.FromProjection(adminProjection);
        AssertEx.False(adminResponse.Urls is string[], "Admin security API URLs should not expose a mutable array.");
        var adminResponseUrls = new List<string>
        {
            adminResponse.Urls[0]
        };
        var directAdminResponse = new RuntimeAdminSecurityResponse(urls: adminResponseUrls, requireAuthentication: true, hasConfiguredToken: true, token: null, tokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", tokenSource: "configured", recentAuditCapacity: 64);
        adminResponseUrls[0] = "http://127.0.0.1:19999";
        adminResponseUrls.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAdminSecurityResponse(urls: null!, requireAuthentication: true, hasConfiguredToken: true, token: null, tokenEnvironmentVariable: "MDRAVA_ADMIN_TOKEN", tokenSource: "configured", recentAuditCapacity: 64));
        AssertEx.Equal("http://127.0.0.1:18082", directAdminResponse.Urls[0]);
        AssertEx.False(directAdminResponse.Urls is string[], "Direct admin security API URLs should not expose a mutable array.");
        var forwardedHeadersResponse = RuntimeForwardedHeadersResponseMapper.FromProjection(forwardedHeadersProjection);
        AssertEx.False(forwardedHeadersResponse.TrustedProxies is string[], "Forwarded headers API trusted proxies should not expose a mutable array.");
        var forwardedHeaderTrustedProxies = new List<string>
        {
            forwardedHeadersResponse.TrustedProxies[0]
        };
        var directForwardedHeadersResponse = new RuntimeForwardedHeadersResponse(enabled: forwardedHeadersResponse.Enabled, trustedProxies: forwardedHeaderTrustedProxies);
        forwardedHeaderTrustedProxies[0] = "10.0.0.1";
        forwardedHeaderTrustedProxies.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeForwardedHeadersResponse(enabled: true, trustedProxies: null!));
        AssertEx.Equal("127.0.0.1", directForwardedHeadersResponse.TrustedProxies[0]);
        AssertEx.False(directForwardedHeadersResponse.TrustedProxies is string[], "Direct forwarded headers API trusted proxies should not expose a mutable array.");
        var headerPolicyResponse = RuntimeHeaderPolicyResponseMapper.FromProjection(headerPolicyProjection);
        AssertEx.False(headerPolicyResponse.SetRequestHeaders is RuntimeHeaderFieldResponse[], "Header policy API set request headers should not expose a mutable array.");
        AssertEx.False(headerPolicyResponse.RemoveRequestHeaders is string[], "Header policy API remove request headers should not expose a mutable array.");
        AssertEx.False(headerPolicyResponse.SetResponseHeaders is RuntimeHeaderFieldResponse[], "Header policy API set response headers should not expose a mutable array.");
        AssertEx.False(headerPolicyResponse.RemoveResponseHeaders is string[], "Header policy API remove response headers should not expose a mutable array.");
        var headerSetRequest = new List<RuntimeHeaderFieldResponse>
        {
            headerPolicyResponse.SetRequestHeaders[0]
        };
        var headerRemoveRequest = new List<string>
        {
            headerPolicyResponse.RemoveRequestHeaders[0]
        };
        var headerSetResponse = new List<RuntimeHeaderFieldResponse>
        {
            headerPolicyResponse.SetResponseHeaders[0]
        };
        var headerRemoveResponse = new List<string>
        {
            headerPolicyResponse.RemoveResponseHeaders[0]
        };
        var directHeaderPolicyResponse = new RuntimeHeaderPolicyResponse(setRequestHeaders: headerSetRequest, removeRequestHeaders: headerRemoveRequest, setResponseHeaders: headerSetResponse, removeResponseHeaders: headerRemoveResponse);
        headerSetRequest[0] = headerSetRequest[0] with
        {
            Name = "X-Replacement-Request"
        };
        headerRemoveRequest[0] = "X-Replacement-Remove-Request";
        headerSetResponse[0] = headerSetResponse[0] with
        {
            Name = "X-Replacement-Response"
        };
        headerRemoveResponse[0] = "X-Replacement-Remove-Response";
        headerSetRequest.Clear();
        headerRemoveRequest.Clear();
        headerSetResponse.Clear();
        headerRemoveResponse.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHeaderPolicyResponse(setRequestHeaders: null!, removeRequestHeaders: [], setResponseHeaders: [], removeResponseHeaders: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHeaderPolicyResponse(setRequestHeaders: [], removeRequestHeaders: null!, setResponseHeaders: [], removeResponseHeaders: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHeaderPolicyResponse(setRequestHeaders: [], removeRequestHeaders: [], setResponseHeaders: null!, removeResponseHeaders: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHeaderPolicyResponse(setRequestHeaders: [], removeRequestHeaders: [], setResponseHeaders: [], removeResponseHeaders: null!));
        AssertEx.Equal("X-Trace", directHeaderPolicyResponse.SetRequestHeaders[0].Name);
        AssertEx.Equal("X-Remove-Request", directHeaderPolicyResponse.RemoveRequestHeaders[0]);
        AssertEx.Equal("X-Frame-Options", directHeaderPolicyResponse.SetResponseHeaders[0].Name);
        AssertEx.Equal("Server", directHeaderPolicyResponse.RemoveResponseHeaders[0]);
        AssertEx.False(directHeaderPolicyResponse.SetRequestHeaders is RuntimeHeaderFieldResponse[], "Direct header policy API set request headers should not expose a mutable array.");
        AssertEx.False(directHeaderPolicyResponse.RemoveRequestHeaders is string[], "Direct header policy API remove request headers should not expose a mutable array.");
        AssertEx.False(directHeaderPolicyResponse.SetResponseHeaders is RuntimeHeaderFieldResponse[], "Direct header policy API set response headers should not expose a mutable array.");
        AssertEx.False(directHeaderPolicyResponse.RemoveResponseHeaders is string[], "Direct header policy API remove response headers should not expose a mutable array.");
        var cacheResponse = RuntimeCachePolicyResponseMapper.FromProjection(cacheProjection);
        AssertEx.False(cacheResponse.VaryByHeaders is string[], "Cache API vary headers should not expose a mutable array.");
        AssertEx.False(cacheResponse.CacheableStatusCodes is int[], "Cache API cacheable status codes should not expose a mutable array.");
        AssertEx.False(cacheResponse.Methods is string[], "Cache API methods should not expose a mutable array.");
        var retryResponse = RuntimeRetryPolicyResponseMapper.FromProjection(retryProjection);
        AssertEx.False(retryResponse.RetryOnStatusCodes is int[], "Retry API status codes should not expose a mutable array.");
        AssertEx.False(retryResponse.RetryMethods is string[], "Retry API methods should not expose a mutable array.");
        var cacheResponseVaryHeaders = new List<string>
        {
            cacheResponse.VaryByHeaders[0]
        };
        var cacheResponseStatusCodes = new List<int>
        {
            cacheResponse.CacheableStatusCodes[0]
        };
        var cacheResponseMethods = new List<string>
        {
            cacheResponse.Methods[0]
        };
        var directCacheResponse = new RuntimeCachePolicyResponse(enabled: cacheResponse.Enabled, maxEntryBytes: cacheResponse.MaxEntryBytes, maxTotalBytes: cacheResponse.MaxTotalBytes, defaultTtl: cacheResponse.DefaultTtl, respectOriginCacheControl: cacheResponse.RespectOriginCacheControl, varyByHeaders: cacheResponseVaryHeaders, cacheableStatusCodes: cacheResponseStatusCodes, methods: cacheResponseMethods);
        var retryResponseStatusCodes = new List<int>
        {
            retryResponse.RetryOnStatusCodes[0]
        };
        var retryResponseMethods = new List<string>
        {
            retryResponse.RetryMethods[0]
        };
        var directRetryResponse = new RuntimeRetryPolicyResponse(enabled: retryResponse.Enabled, maxAttempts: retryResponse.MaxAttempts, perAttemptTimeout: retryResponse.PerAttemptTimeout, retryOnConnectFailure: retryResponse.RetryOnConnectFailure, retryOnUpstreamResponseHeadTimeout: retryResponse.RetryOnUpstreamResponseHeadTimeout, retryOnStatusCodes: retryResponseStatusCodes, retryMethods: retryResponseMethods, retryBackoff: retryResponse.RetryBackoff);
        cacheResponseVaryHeaders[0] = "X-Replacement";
        cacheResponseStatusCodes[0] = 299;
        cacheResponseMethods[0] = "POST";
        retryResponseStatusCodes[0] = 599;
        retryResponseMethods[0] = "POST";
        cacheResponseVaryHeaders.Clear();
        cacheResponseStatusCodes.Clear();
        cacheResponseMethods.Clear();
        retryResponseStatusCodes.Clear();
        retryResponseMethods.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCachePolicyResponse(enabled: true, maxEntryBytes: 1024, maxTotalBytes: 4096, defaultTtl: TimeSpan.FromSeconds(60), respectOriginCacheControl: true, varyByHeaders: null!, cacheableStatusCodes: [], methods: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCachePolicyResponse(enabled: true, maxEntryBytes: 1024, maxTotalBytes: 4096, defaultTtl: TimeSpan.FromSeconds(60), respectOriginCacheControl: true, varyByHeaders: [], cacheableStatusCodes: null!, methods: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCachePolicyResponse(enabled: true, maxEntryBytes: 1024, maxTotalBytes: 4096, defaultTtl: TimeSpan.FromSeconds(60), respectOriginCacheControl: true, varyByHeaders: [], cacheableStatusCodes: [], methods: null!));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRetryPolicyResponse(enabled: true, maxAttempts: 3, perAttemptTimeout: TimeSpan.FromSeconds(1), retryOnConnectFailure: true, retryOnUpstreamResponseHeadTimeout: true, retryOnStatusCodes: null!, retryMethods: [], retryBackoff: TimeSpan.FromMilliseconds(100)));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRetryPolicyResponse(enabled: true, maxAttempts: 3, perAttemptTimeout: TimeSpan.FromSeconds(1), retryOnConnectFailure: true, retryOnUpstreamResponseHeadTimeout: true, retryOnStatusCodes: [], retryMethods: null!, retryBackoff: TimeSpan.FromMilliseconds(100)));
        AssertEx.Equal("X-Tenant", directCacheResponse.VaryByHeaders[0]);
        AssertEx.Equal(200, directCacheResponse.CacheableStatusCodes[0]);
        AssertEx.Equal("GET", directCacheResponse.Methods[0]);
        AssertEx.Equal(502, directRetryResponse.RetryOnStatusCodes[0]);
        AssertEx.Equal("GET", directRetryResponse.RetryMethods[0]);
        AssertEx.False(directCacheResponse.VaryByHeaders is string[], "Direct cache API vary headers should not expose a mutable array.");
        AssertEx.False(directCacheResponse.CacheableStatusCodes is int[], "Direct cache API cacheable status codes should not expose a mutable array.");
        AssertEx.False(directCacheResponse.Methods is string[], "Direct cache API methods should not expose a mutable array.");
        AssertEx.False(directRetryResponse.RetryOnStatusCodes is int[], "Direct retry API status codes should not expose a mutable array.");
        AssertEx.False(directRetryResponse.RetryMethods is string[], "Direct retry API methods should not expose a mutable array.");
        var acmeResponse = RuntimeAcmeResponseMapper.FromProjection(acmeProjection);
        AssertEx.False(acmeResponse.ContactEmails is string[], "ACME API contact emails should not expose a mutable array.");
        AssertEx.False(acmeResponse.Certificates is RuntimeAcmeCertificateResponse[], "ACME API certificates should not expose a mutable array.");
        AssertEx.False(acmeResponse.Certificates[0].Domains is string[], "ACME API certificate domains should not expose a mutable array.");
        var certificateResponses = RuntimeCertificateResponseMapper.FromCertificates([certificateProjection]);
        AssertEx.False(certificateResponses is RuntimeCertificateResponse[], "Configuration API certificates should not expose a mutable array.");
        AssertEx.False(certificateResponses[0].Domains is string[], "Configuration API certificate domains should not expose a mutable array.");
        var acmeResponseContacts = new List<string>
        {
            acmeResponse.ContactEmails[0]
        };
        var acmeCertificateDomains = new List<string>
        {
            acmeResponse.Certificates[0].Domains[0]
        };
        var directAcmeCertificate = new RuntimeAcmeCertificateResponse(id: acmeResponse.Certificates[0].Id, enabled: acmeResponse.Certificates[0].Enabled, domains: acmeCertificateDomains, renewBeforeDays: acmeResponse.Certificates[0].RenewBeforeDays);
        var acmeResponseCertificates = new List<RuntimeAcmeCertificateResponse>
        {
            directAcmeCertificate
        };
        var directAcmeResponse = new RuntimeAcmeResponse(enabled: acmeResponse.Enabled, useStaging: acmeResponse.UseStaging, directoryUrl: acmeResponse.DirectoryUrl, contactEmails: acmeResponseContacts, termsAccepted: acmeResponse.TermsAccepted, storagePath: acmeResponse.StoragePath, renewBeforeDays: acmeResponse.RenewBeforeDays, checkIntervalMinutes: acmeResponse.CheckIntervalMinutes, retryAfterMinutes: acmeResponse.RetryAfterMinutes, certificates: acmeResponseCertificates);
        var certificateResponseDomains = new List<string>
        {
            certificateResponses[0].Domains[0]
        };
        var directCertificateResponse = new RuntimeCertificateResponse(id: certificateResponses[0].Id, path: certificateResponses[0].Path, format: certificateResponses[0].Format, source: certificateResponses[0].Source, domains: certificateResponseDomains, hasConfiguredPassword: certificateResponses[0].HasConfiguredPassword, subject: certificateResponses[0].Subject, thumbprint: certificateResponses[0].Thumbprint, notBefore: certificateResponses[0].NotBefore, notAfter: certificateResponses[0].NotAfter);
        acmeResponseContacts[0] = "replacement@home.test";
        acmeCertificateDomains[0] = "replacement.home.test";
        acmeResponseCertificates[0] = new RuntimeAcmeCertificateResponse(id: "replacement-cert", enabled: false, domains: ["replacement.home.test"], renewBeforeDays: 1);
        certificateResponseDomains[0] = "replacement.home.test";
        acmeResponseContacts.Clear();
        acmeCertificateDomains.Clear();
        acmeResponseCertificates.Clear();
        certificateResponseDomains.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAcmeResponse(enabled: true, useStaging: false, directoryUrl: "https://acme.test/directory", contactEmails: null!, termsAccepted: true, storagePath: "acme", renewBeforeDays: 14, checkIntervalMinutes: 60, retryAfterMinutes: 10, certificates: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAcmeResponse(enabled: true, useStaging: false, directoryUrl: "https://acme.test/directory", contactEmails: [], termsAccepted: true, storagePath: "acme", renewBeforeDays: 14, checkIntervalMinutes: 60, retryAfterMinutes: 10, certificates: null!));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeAcmeCertificateResponse(id: "api-cert", enabled: true, domains: null!, renewBeforeDays: 14));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificateResponse(id: "api-cert", path: "certs/api.pfx", format: "pfx", source: "manual", domains: null!, hasConfiguredPassword: false, subject: null, thumbprint: null, notBefore: DateTime.UnixEpoch, notAfter: DateTime.UnixEpoch.AddDays(1)));
        AssertEx.Equal("ops@home.test", directAcmeResponse.ContactEmails[0]);
        AssertEx.Equal("api-cert", directAcmeResponse.Certificates[0].Id);
        AssertEx.Equal("api.home.test", directAcmeCertificate.Domains[0]);
        AssertEx.Equal("api.home.test", directCertificateResponse.Domains[0]);
        AssertEx.False(directAcmeResponse.ContactEmails is string[], "Direct ACME API contact emails should not expose a mutable array.");
        AssertEx.False(directAcmeResponse.Certificates is RuntimeAcmeCertificateResponse[], "Direct ACME API certificates should not expose a mutable array.");
        AssertEx.False(directAcmeCertificate.Domains is string[], "Direct ACME API certificate domains should not expose a mutable array.");
        AssertEx.False(directCertificateResponse.Domains is string[], "Direct configuration API certificate domains should not expose a mutable array.");
    }

    public static void RuntimeConfigurationGraphRecordsCopyInputCollections()
    {
        var sourceFiles = new List<string>
        {
            "sites/home.json"
        };
        var sniCertificates = new List<RuntimeSniCertificateBinding>
        {
            new("home.test", "home-cert")
        };
        var upstreams = new List<RuntimeUpstream>
        {
            new("home", "local", "http", RuntimeUpstreamProtocol.Http1, "127.0.0.1", 5000, 1, RuntimeUpstreamTlsOptions.Default)
        };
        using var certificate = X509CertificateLoader.LoadPkcs12(TestCertificates.CreateSelfSignedPfxBytes("home.test"), password: null);
        var runtimeCertificate = new RuntimeCertificate("home-cert", "certs/home.pfx", "pfx", HasConfiguredPassword: false, certificate, "manual", ["home.test"]);
        var certificates = new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase)
        {
            ["home-cert"] = runtimeCertificate
        };
        var listener = new RuntimeListener("web", "127.0.0.1", 18080, Enabled: true, RuntimeListenerTransport.Https, DefaultCertificateId: "home-cert", sniCertificates, Backlog: 512, MaxRequestHeadBytes: 32768, MaxResponseHeadBytes: 32768, MaxChunkLineBytes: 8192, ForwardingBufferBytes: 8192);
        var route = new RuntimeRoute("home", "home.test", "/", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(false, "/health", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1, 1), upstreams, new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", "ok"), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), RuntimeCachePolicy.Disabled, new RuntimeRouteResolvedOptions(104857600, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true), SiteName: "home", Retry: RuntimeRetryPolicy.Disabled);
        var listeners = new List<RuntimeListener>
        {
            listener
        };
        var routes = new List<RuntimeRoute>
        {
            route
        };
        var snapshot = new ProxyConfigurationSnapshot(1, DateTimeOffset.UnixEpoch, "tests", sourceFiles, new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []), new RuntimeAdminSecurityOptions([], true, true, "secret", "MDRAVA_ADMIN_TOKEN", "configured", 100), new RuntimeAcmeOptions(false, true, "", [], false, "acme", 30, 720, 60, []), new RuntimeTimeouts(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), new RuntimeConnectionLimits(100, 16, 1024), new RuntimeObservabilityOptions(true, 100, new RuntimeLogPersistenceOptions(true, true, 1_048_576, 8)), new RuntimeLimits(4096, 128, 240, 30, 32768, 128, 8192, 104857600, 8192, TimeSpan.FromSeconds(15)), new RuntimeForwardedHeadersOptions(true, []), certificates, listeners, routes);
        sourceFiles.Clear();
        sniCertificates.Clear();
        upstreams.Clear();
        certificates.Clear();
        listeners.Clear();
        routes.Clear();
        AssertEx.Equal("home.test", listener.SniCertificates[0].HostName);
        AssertEx.Equal("local", route.Upstreams[0].Name);
        AssertEx.Equal("sites/home.json", snapshot.SourceFiles[0]);
        AssertEx.True(snapshot.Certificates.ContainsKey("HOME-CERT"));
        AssertEx.Equal("home-cert", snapshot.Certificates["HOME-CERT"].Id);
        AssertEx.Equal("web", snapshot.Listeners[0].Name);
        AssertEx.Equal("home", snapshot.Routes[0].Name);
        AssertEx.False(listener.SniCertificates is RuntimeSniCertificateBinding[]);
        AssertEx.False(route.Upstreams is RuntimeUpstream[]);
        AssertEx.False(snapshot.SourceFiles is string[]);
        AssertEx.False(snapshot.Certificates is Dictionary<string, RuntimeCertificate>);
        AssertEx.False(snapshot.Listeners is RuntimeListener[]);
        AssertEx.False(snapshot.Routes is RuntimeRoute[]);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyConfigurationSnapshot(0, snapshot.LoadedAtUtc, snapshot.SourceDirectory, snapshot.SourceFiles, snapshot.Discovery, snapshot.AdminSecurity, snapshot.Acme, snapshot.Timeouts, snapshot.ConnectionLimits, snapshot.Observability, snapshot.Limits, snapshot.ForwardedHeaders, snapshot.Certificates, snapshot.Listeners, snapshot.Routes, snapshot.Metrics));
        AssertConnectionLimitsRejects(maxRequestsPerClientConnection: 0);
        AssertConnectionLimitsRejects(maxIdleUpstreamConnectionsPerUpstream: -1);
        AssertConnectionLimitsRejects(maxActiveUpgradedTunnels: 0);
        AssertConnectionLimitsProjectionRejects(maxRequestsPerClientConnection: 0);
        AssertConnectionLimitsProjectionRejects(maxIdleUpstreamConnectionsPerUpstream: -1);
        AssertConnectionLimitsProjectionRejects(maxActiveUpgradedTunnels: 0);
        AssertLimitsRejects(maxActiveClientConnections: 0);
        AssertLimitsRejects(maxConcurrentTlsHandshakes: 0);
        AssertLimitsRejects(requestsPerMinutePerIp: 0);
        AssertLimitsRejects(upgradeRequestsPerMinutePerIp: 0);
        AssertLimitsRejects(maxRequestHeadBytes: 0);
        AssertLimitsRejects(maxHeaderCount: 0);
        AssertLimitsRejects(maxHeaderLineBytes: 0);
        AssertLimitsRejects(maxRequestBodyBytes: -1);
        AssertLimitsRejects(maxPathBytes: 0);
        AssertLimitsRejects(shutdownGracePeriod: TimeSpan.Zero);
        AssertLimitsProjectionRejects(maxActiveClientConnections: 0);
        AssertLimitsProjectionRejects(maxConcurrentTlsHandshakes: 0);
        AssertLimitsProjectionRejects(requestsPerMinutePerIp: 0);
        AssertLimitsProjectionRejects(upgradeRequestsPerMinutePerIp: 0);
        AssertLimitsProjectionRejects(maxRequestHeadBytes: 0);
        AssertLimitsProjectionRejects(maxHeaderCount: 0);
        AssertLimitsProjectionRejects(maxHeaderLineBytes: 0);
        AssertLimitsProjectionRejects(maxRequestBodyBytes: -1);
        AssertLimitsProjectionRejects(maxPathBytes: 0);
        AssertLimitsProjectionRejects(shutdownGracePeriod: TimeSpan.Zero);
        AssertEx.Throws<ArgumentNullException>(() => listener.WithSniCertificates([null!]));
        AssertEx.Throws<ArgumentNullException>(() => route.WithUpstreams([null!]));
        AssertEx.Throws<ArgumentNullException>(() => snapshot.WithListenersAndRoutes([null!], snapshot.Routes));
        AssertEx.Throws<ArgumentNullException>(() => snapshot.WithListenersAndRoutes(snapshot.Listeners, [null!]));
        var http3Projection = new RuntimeHttp3SupportProjection("unknown", QuicListenerSupported: false, QuicConnectionSupported: false, "disabled", "disabled", EnabledForTraffic: false, QuicListenerReady: false, AltSvcConfigured: false, AltSvcActive: false, AltSvcMaxAgeSeconds: null, "not_configured", UdpQuicListenerIdentityModeled: true, "not_ready");
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, http3Projection);
        AssertEx.Equal("sites/home.json", projection.SourceFiles[0]);
        AssertEx.Equal("home-cert", projection.Certificates[0].Id);
        AssertEx.Equal("web", projection.Listeners[0].Name);
        AssertEx.Equal("home", projection.Routes[0].Name);
        AssertEx.False(projection.SourceFiles is string[]);
        AssertEx.False(projection.Certificates is RuntimeCertificateProjection[]);
        AssertEx.False(projection.Listeners is RuntimeListenerProjection[]);
        AssertEx.False(projection.Routes is RuntimeRouteProjection[]);
        AssertEx.True(projection.Metrics.Enabled);
        AssertEx.Equal("not_ready", projection.Http3.ReadinessConclusion);
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(null!, http3Projection));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(snapshot, null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(SnapshotWith(acme: new RuntimeAcmeOptions(false, true, "", [], false, "acme", 30, 720, 60, [null!])), http3Projection));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(SnapshotWith(certificates: new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase) { ["broken-cert"] = null! }), http3Projection));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(SnapshotWith(listeners: [null!]), http3Projection));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(SnapshotWith(listeners: [listener.WithSniCertificates([null!])]), http3Projection));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(SnapshotWith(routes: [null!]), http3Projection));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(SnapshotWith(routes: [route.WithUpstreams([null!])]), http3Projection));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(SnapshotWith(routes: [RouteWithHeaderPolicy(new RuntimeHeaderPolicy([null!], [], [], []))]), http3Projection));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigurationProjectionMapper.ToProjection(SnapshotWith(routes: [RouteWithHeaderPolicy(new RuntimeHeaderPolicy([], [], [null!], []))]), http3Projection));
        var directProjectionSourceFiles = new List<string>
        {
            projection.SourceFiles[0]
        };
        var directProjectionCertificates = new List<RuntimeCertificateProjection>
        {
            projection.Certificates[0]
        };
        var directProjectionListeners = new List<RuntimeListenerProjection>
        {
            projection.Listeners[0]
        };
        var directProjectionRoutes = new List<RuntimeRouteProjection>
        {
            projection.Routes[0]
        };
        var directProjection = CreateDirectProjection(sourceFiles: directProjectionSourceFiles, metrics: projection.Metrics, http3: projection.Http3, certificates: directProjectionCertificates, listeners: directProjectionListeners, routes: directProjectionRoutes);
        directProjectionSourceFiles[0] = "sites/replacement.json";
        directProjectionCertificates.Clear();
        directProjectionListeners.Clear();
        directProjectionRoutes.Clear();
        AssertEx.Equal("sites/home.json", directProjection.SourceFiles[0]);
        AssertEx.Equal("home-cert", directProjection.Certificates[0].Id);
        AssertEx.Equal("web", directProjection.Listeners[0].Name);
        AssertEx.Equal("home", directProjection.Routes[0].Name);
        AssertEx.False(directProjection.SourceFiles is string[]);
        AssertEx.False(directProjection.Certificates is RuntimeCertificateProjection[]);
        AssertEx.False(directProjection.Listeners is RuntimeListenerProjection[]);
        AssertEx.False(directProjection.Routes is RuntimeRouteProjection[]);
        AssertEx.Throws<ArgumentNullException>(() => CreateDirectProjection(sourceFiles: null!, metrics: projection.Metrics, http3: projection.Http3, certificates: [], listeners: [], routes: []));
        AssertEx.Throws<ArgumentNullException>(() => CreateDirectProjection(sourceFiles: [], metrics: null!, http3: projection.Http3, certificates: [], listeners: [], routes: []));
        AssertEx.Throws<ArgumentNullException>(() => CreateDirectProjection(sourceFiles: [], metrics: projection.Metrics, http3: null!, certificates: [], listeners: [], routes: []));
        AssertEx.Throws<ArgumentNullException>(() => CreateDirectProjection(sourceFiles: [], metrics: projection.Metrics, http3: projection.Http3, certificates: null!, listeners: [], routes: []));
        var response = ProxyConfigurationResponseMapper.FromProjection(projection);
        AssertEx.False(response.SourceFiles is string[], "Configuration API source files should not expose a mutable array.");
        AssertEx.False(response.Certificates is RuntimeCertificateResponse[], "Configuration API certificates should not expose a mutable array.");
        AssertEx.False(response.Listeners is RuntimeListenerResponse[], "Configuration API listeners should not expose a mutable array.");
        AssertEx.False(response.Routes is RuntimeRouteResponse[], "Configuration API routes should not expose a mutable array.");
        var responseSourceFiles = new List<string>
        {
            response.SourceFiles[0]
        };
        var responseCertificates = new List<RuntimeCertificateResponse>
        {
            response.Certificates[0]
        };
        var responseListeners = new List<RuntimeListenerResponse>
        {
            response.Listeners[0]
        };
        var responseRoutes = new List<RuntimeRouteResponse>
        {
            response.Routes[0]
        };
        var directResponse = new ProxyConfigurationResponse(version: response.Version, loadedAtUtc: response.LoadedAtUtc, sourceDirectory: response.SourceDirectory, sourceFiles: responseSourceFiles, discovery: response.Discovery, adminSecurity: response.AdminSecurity, acme: response.Acme, timeouts: response.Timeouts, connectionLimits: response.ConnectionLimits, observability: response.Observability, limits: response.Limits, forwardedHeaders: response.ForwardedHeaders, metrics: response.Metrics, http3: response.Http3, certificates: responseCertificates, listeners: responseListeners, routes: responseRoutes);
        responseSourceFiles[0] = "sites/replacement.json";
        responseCertificates[0] = new RuntimeCertificateResponse(id: "replacement-cert", path: responseCertificates[0].Path, format: responseCertificates[0].Format, source: responseCertificates[0].Source, domains: responseCertificates[0].Domains, hasConfiguredPassword: responseCertificates[0].HasConfiguredPassword, subject: responseCertificates[0].Subject, thumbprint: responseCertificates[0].Thumbprint, notBefore: responseCertificates[0].NotBefore, notAfter: responseCertificates[0].NotAfter);
        responseListeners[0] = new RuntimeListenerResponse(name: "replacement-listener", address: responseListeners[0].Address, port: responseListeners[0].Port, enabled: responseListeners[0].Enabled, transport: responseListeners[0].Transport, defaultCertificateId: responseListeners[0].DefaultCertificateId, sniCertificates: responseListeners[0].SniCertificates, backlog: responseListeners[0].Backlog, maxRequestHeadBytes: responseListeners[0].MaxRequestHeadBytes, maxResponseHeadBytes: responseListeners[0].MaxResponseHeadBytes, maxChunkLineBytes: responseListeners[0].MaxChunkLineBytes, forwardingBufferBytes: responseListeners[0].ForwardingBufferBytes, identity: responseListeners[0].Identity, protocols: responseListeners[0].Protocols, http3Enablement: responseListeners[0].Http3Enablement, http3AltSvc: responseListeners[0].Http3AltSvc, http2Limits: responseListeners[0].Http2Limits, tcpTrafficEnabled: responseListeners[0].TcpTrafficEnabled, http3ProtocolConfigured: responseListeners[0].Http3ProtocolConfigured, quicIdentity: responseListeners[0].QuicIdentity, http3: responseListeners[0].Http3);
        responseRoutes[0] = new RuntimeRouteResponse(name: "replacement-route", host: responseRoutes[0].Host, pathPrefix: responseRoutes[0].PathPrefix, action: responseRoutes[0].Action, loadBalancingPolicy: responseRoutes[0].LoadBalancingPolicy, healthCheck: responseRoutes[0].HealthCheck, upstreams: responseRoutes[0].Upstreams, httpsRedirect: responseRoutes[0].HttpsRedirect, canonicalHost: responseRoutes[0].CanonicalHost, headerPolicy: responseRoutes[0].HeaderPolicy, pathRewrite: responseRoutes[0].PathRewrite, redirect: responseRoutes[0].Redirect, staticResponse: responseRoutes[0].StaticResponse, maintenance: responseRoutes[0].Maintenance, cache: responseRoutes[0].Cache, resolvedOptions: responseRoutes[0].ResolvedOptions, siteName: responseRoutes[0].SiteName, retry: responseRoutes[0].Retry);
        responseSourceFiles.Clear();
        responseCertificates.Clear();
        responseListeners.Clear();
        responseRoutes.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationResponse(version: response.Version, loadedAtUtc: response.LoadedAtUtc, sourceDirectory: response.SourceDirectory, sourceFiles: null!, discovery: response.Discovery, adminSecurity: response.AdminSecurity, acme: response.Acme, timeouts: response.Timeouts, connectionLimits: response.ConnectionLimits, observability: response.Observability, limits: response.Limits, forwardedHeaders: response.ForwardedHeaders, metrics: response.Metrics, http3: response.Http3, certificates: [], listeners: [], routes: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationResponse(version: response.Version, loadedAtUtc: response.LoadedAtUtc, sourceDirectory: response.SourceDirectory, sourceFiles: [], discovery: null!, adminSecurity: response.AdminSecurity, acme: response.Acme, timeouts: response.Timeouts, connectionLimits: response.ConnectionLimits, observability: response.Observability, limits: response.Limits, forwardedHeaders: response.ForwardedHeaders, metrics: response.Metrics, http3: response.Http3, certificates: [], listeners: [], routes: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationResponse(version: response.Version, loadedAtUtc: response.LoadedAtUtc, sourceDirectory: response.SourceDirectory, sourceFiles: [], discovery: response.Discovery, adminSecurity: response.AdminSecurity, acme: response.Acme, timeouts: response.Timeouts, connectionLimits: response.ConnectionLimits, observability: response.Observability, limits: response.Limits, forwardedHeaders: response.ForwardedHeaders, metrics: null!, http3: response.Http3, certificates: [], listeners: [], routes: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationResponse(version: response.Version, loadedAtUtc: response.LoadedAtUtc, sourceDirectory: response.SourceDirectory, sourceFiles: [], discovery: response.Discovery, adminSecurity: response.AdminSecurity, acme: response.Acme, timeouts: response.Timeouts, connectionLimits: response.ConnectionLimits, observability: response.Observability, limits: response.Limits, forwardedHeaders: response.ForwardedHeaders, metrics: response.Metrics, http3: null!, certificates: [], listeners: [], routes: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationResponse(version: response.Version, loadedAtUtc: response.LoadedAtUtc, sourceDirectory: response.SourceDirectory, sourceFiles: [], discovery: response.Discovery, adminSecurity: response.AdminSecurity, acme: response.Acme, timeouts: response.Timeouts, connectionLimits: response.ConnectionLimits, observability: response.Observability, limits: response.Limits, forwardedHeaders: response.ForwardedHeaders, metrics: response.Metrics, http3: response.Http3, certificates: null!, listeners: [], routes: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationResponse(version: response.Version, loadedAtUtc: response.LoadedAtUtc, sourceDirectory: response.SourceDirectory, sourceFiles: [], discovery: response.Discovery, adminSecurity: response.AdminSecurity, acme: response.Acme, timeouts: response.Timeouts, connectionLimits: response.ConnectionLimits, observability: response.Observability, limits: response.Limits, forwardedHeaders: response.ForwardedHeaders, metrics: response.Metrics, http3: response.Http3, certificates: [], listeners: null!, routes: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigurationResponse(version: response.Version, loadedAtUtc: response.LoadedAtUtc, sourceDirectory: response.SourceDirectory, sourceFiles: [], discovery: response.Discovery, adminSecurity: response.AdminSecurity, acme: response.Acme, timeouts: response.Timeouts, connectionLimits: response.ConnectionLimits, observability: response.Observability, limits: response.Limits, forwardedHeaders: response.ForwardedHeaders, metrics: response.Metrics, http3: response.Http3, certificates: [], listeners: [], routes: null!));
        AssertEx.Equal("sites/home.json", directResponse.SourceFiles[0]);
        AssertEx.Equal("home-cert", directResponse.Certificates[0].Id);
        AssertEx.Equal("web", directResponse.Listeners[0].Name);
        AssertEx.Equal("home", directResponse.Routes[0].Name);
        AssertEx.False(directResponse.SourceFiles is string[], "Direct configuration API source files should not expose a mutable array.");
        AssertEx.False(directResponse.Certificates is RuntimeCertificateResponse[], "Direct configuration API certificates should not expose a mutable array.");
        AssertEx.False(directResponse.Listeners is RuntimeListenerResponse[], "Direct configuration API listeners should not expose a mutable array.");
        AssertEx.False(directResponse.Routes is RuntimeRouteResponse[], "Direct configuration API routes should not expose a mutable array.");
        ProxyConfigurationProjection CreateDirectProjection(IReadOnlyList<string> sourceFiles, RuntimeMetricsProjection metrics, RuntimeHttp3SupportProjection http3, IReadOnlyList<RuntimeCertificateProjection> certificates, IReadOnlyList<RuntimeListenerProjection> listeners, IReadOnlyList<RuntimeRouteProjection> routes)
        {
            return new ProxyConfigurationProjection(projection.Version, projection.LoadedAtUtc, projection.SourceDirectory, sourceFiles, projection.Discovery, projection.AdminSecurity, projection.Acme, projection.Timeouts, projection.ConnectionLimits, projection.Observability, projection.Limits, projection.ForwardedHeaders, metrics, http3, certificates, listeners, routes);
        }

        ProxyConfigurationSnapshot SnapshotWith(RuntimeAcmeOptions? acme = null, IReadOnlyDictionary<string, RuntimeCertificate>? certificates = null, IReadOnlyList<RuntimeListener>? listeners = null, IReadOnlyList<RuntimeRoute>? routes = null)
        {
            return new ProxyConfigurationSnapshot(snapshot.Version, snapshot.LoadedAtUtc, snapshot.SourceDirectory, snapshot.SourceFiles, snapshot.Discovery, snapshot.AdminSecurity, acme ?? snapshot.Acme, snapshot.Timeouts, snapshot.ConnectionLimits, snapshot.Observability, snapshot.Limits, snapshot.ForwardedHeaders, certificates ?? snapshot.Certificates, listeners ?? snapshot.Listeners, routes ?? snapshot.Routes, snapshot.Metrics);
        }

        RuntimeRoute RouteWithHeaderPolicy(RuntimeHeaderPolicy headerPolicy)
        {
            return new RuntimeRoute(route.Name, route.Host, route.PathPrefix, route.Action, route.LoadBalancingPolicy, route.HealthCheck, route.Upstreams, route.HttpsRedirect, route.CanonicalHost, headerPolicy, route.PathRewrite, route.Redirect, route.StaticResponse, route.Maintenance, route.Cache, route.ResolvedOptions, route.SiteName, route.Retry);
        }

        static void AssertConnectionLimitsRejects(int maxRequestsPerClientConnection = 100, int maxIdleUpstreamConnectionsPerUpstream = 16, int maxActiveUpgradedTunnels = 1024)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeConnectionLimits(maxRequestsPerClientConnection, maxIdleUpstreamConnectionsPerUpstream, maxActiveUpgradedTunnels));
        }

        static void AssertConnectionLimitsProjectionRejects(int maxRequestsPerClientConnection = 100, int maxIdleUpstreamConnectionsPerUpstream = 16, int maxActiveUpgradedTunnels = 1024)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeConnectionLimitsProjection(maxRequestsPerClientConnection, maxIdleUpstreamConnectionsPerUpstream, maxActiveUpgradedTunnels));
        }

        static void AssertLimitsRejects(int maxActiveClientConnections = 4096, int maxConcurrentTlsHandshakes = 128, int requestsPerMinutePerIp = 240, int upgradeRequestsPerMinutePerIp = 30, int maxRequestHeadBytes = 32768, int maxHeaderCount = 128, int maxHeaderLineBytes = 8192, long maxRequestBodyBytes = 104857600, int maxPathBytes = 8192, TimeSpan? shutdownGracePeriod = null)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeLimits(maxActiveClientConnections, maxConcurrentTlsHandshakes, requestsPerMinutePerIp, upgradeRequestsPerMinutePerIp, maxRequestHeadBytes, maxHeaderCount, maxHeaderLineBytes, maxRequestBodyBytes, maxPathBytes, shutdownGracePeriod ?? TimeSpan.FromSeconds(15)));
        }

        static void AssertLimitsProjectionRejects(int maxActiveClientConnections = 4096, int maxConcurrentTlsHandshakes = 128, int requestsPerMinutePerIp = 240, int upgradeRequestsPerMinutePerIp = 30, int maxRequestHeadBytes = 32768, int maxHeaderCount = 128, int maxHeaderLineBytes = 8192, long maxRequestBodyBytes = 104857600, int maxPathBytes = 8192, TimeSpan? shutdownGracePeriod = null)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeLimitsProjection(maxActiveClientConnections, maxConcurrentTlsHandshakes, requestsPerMinutePerIp, upgradeRequestsPerMinutePerIp, maxRequestHeadBytes, maxHeaderCount, maxHeaderLineBytes, maxRequestBodyBytes, maxPathBytes, shutdownGracePeriod ?? TimeSpan.FromSeconds(15)));
        }

        var listenerResponses = RuntimeListenerResponseMapper.FromListeners(projection.Listeners);
        AssertEx.False(listenerResponses is RuntimeListenerResponse[], "Configuration API listeners should not expose a mutable array.");
        AssertEx.False(listenerResponses[0].SniCertificates is RuntimeSniCertificateBindingResponse[], "Configuration API listener SNI certificates should not expose a mutable array.");
        var directSniCertificates = new List<RuntimeSniCertificateBindingResponse>
        {
            listenerResponses[0].SniCertificates[0]
        };
        var directListenerResponse = new RuntimeListenerResponse(name: "web", address: listenerResponses[0].Address, port: listenerResponses[0].Port, enabled: listenerResponses[0].Enabled, transport: listenerResponses[0].Transport, defaultCertificateId: listenerResponses[0].DefaultCertificateId, sniCertificates: directSniCertificates, backlog: listenerResponses[0].Backlog, maxRequestHeadBytes: listenerResponses[0].MaxRequestHeadBytes, maxResponseHeadBytes: listenerResponses[0].MaxResponseHeadBytes, maxChunkLineBytes: listenerResponses[0].MaxChunkLineBytes, forwardingBufferBytes: listenerResponses[0].ForwardingBufferBytes, identity: listenerResponses[0].Identity, protocols: listenerResponses[0].Protocols, http3Enablement: listenerResponses[0].Http3Enablement, http3AltSvc: listenerResponses[0].Http3AltSvc, http2Limits: listenerResponses[0].Http2Limits, tcpTrafficEnabled: listenerResponses[0].TcpTrafficEnabled, http3ProtocolConfigured: listenerResponses[0].Http3ProtocolConfigured, quicIdentity: listenerResponses[0].QuicIdentity, http3: listenerResponses[0].Http3);
        directSniCertificates[0] = new RuntimeSniCertificateBindingResponse("replacement.test", "replacement-cert");
        directSniCertificates.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeListenerResponse(name: "web", address: listenerResponses[0].Address, port: listenerResponses[0].Port, enabled: listenerResponses[0].Enabled, transport: listenerResponses[0].Transport, defaultCertificateId: listenerResponses[0].DefaultCertificateId, sniCertificates: null!, backlog: listenerResponses[0].Backlog, maxRequestHeadBytes: listenerResponses[0].MaxRequestHeadBytes, maxResponseHeadBytes: listenerResponses[0].MaxResponseHeadBytes, maxChunkLineBytes: listenerResponses[0].MaxChunkLineBytes, forwardingBufferBytes: listenerResponses[0].ForwardingBufferBytes, identity: listenerResponses[0].Identity, protocols: listenerResponses[0].Protocols, http3Enablement: listenerResponses[0].Http3Enablement, http3AltSvc: listenerResponses[0].Http3AltSvc, http2Limits: listenerResponses[0].Http2Limits, tcpTrafficEnabled: listenerResponses[0].TcpTrafficEnabled, http3ProtocolConfigured: listenerResponses[0].Http3ProtocolConfigured, quicIdentity: listenerResponses[0].QuicIdentity, http3: listenerResponses[0].Http3));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeListenerResponse(name: "web", address: listenerResponses[0].Address, port: listenerResponses[0].Port, enabled: listenerResponses[0].Enabled, transport: listenerResponses[0].Transport, defaultCertificateId: listenerResponses[0].DefaultCertificateId, sniCertificates: [], backlog: listenerResponses[0].Backlog, maxRequestHeadBytes: listenerResponses[0].MaxRequestHeadBytes, maxResponseHeadBytes: listenerResponses[0].MaxResponseHeadBytes, maxChunkLineBytes: listenerResponses[0].MaxChunkLineBytes, forwardingBufferBytes: listenerResponses[0].ForwardingBufferBytes, identity: null!, protocols: listenerResponses[0].Protocols, http3Enablement: listenerResponses[0].Http3Enablement, http3AltSvc: listenerResponses[0].Http3AltSvc, http2Limits: listenerResponses[0].Http2Limits, tcpTrafficEnabled: listenerResponses[0].TcpTrafficEnabled, http3ProtocolConfigured: listenerResponses[0].Http3ProtocolConfigured, quicIdentity: listenerResponses[0].QuicIdentity, http3: listenerResponses[0].Http3));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeListenerResponse(name: "web", address: listenerResponses[0].Address, port: listenerResponses[0].Port, enabled: listenerResponses[0].Enabled, transport: listenerResponses[0].Transport, defaultCertificateId: listenerResponses[0].DefaultCertificateId, sniCertificates: [], backlog: listenerResponses[0].Backlog, maxRequestHeadBytes: listenerResponses[0].MaxRequestHeadBytes, maxResponseHeadBytes: listenerResponses[0].MaxResponseHeadBytes, maxChunkLineBytes: listenerResponses[0].MaxChunkLineBytes, forwardingBufferBytes: listenerResponses[0].ForwardingBufferBytes, identity: listenerResponses[0].Identity, protocols: listenerResponses[0].Protocols, http3Enablement: listenerResponses[0].Http3Enablement, http3AltSvc: listenerResponses[0].Http3AltSvc, http2Limits: listenerResponses[0].Http2Limits, tcpTrafficEnabled: listenerResponses[0].TcpTrafficEnabled, http3ProtocolConfigured: listenerResponses[0].Http3ProtocolConfigured, quicIdentity: listenerResponses[0].QuicIdentity, http3: null!));
        AssertEx.Equal("home.test", directListenerResponse.SniCertificates[0].HostName);
        AssertEx.Equal("web", directListenerResponse.Name);
        AssertEx.False(directListenerResponse.SniCertificates is RuntimeSniCertificateBindingResponse[], "Direct configuration API listener SNI certificates should not expose a mutable array.");
        var directListenerIdentityResponse = new RuntimeListenerIdentityResponse(name: "web", address: "127.0.0.1", port: 18080, transport: RuntimeListenerTransportResponse.Https, tlsEnabled: true, key: "web|127.0.0.1|18080|https", bindKey: "127.0.0.1|18080|https");
        var directQuicIdentityResponse = new RuntimeQuicListenerIdentityResponse(name: "web", address: "127.0.0.1", port: 18080, tlsEnabled: true, key: "web|127.0.0.1|18080|udp|quic", bindKey: "127.0.0.1|18080|udp|quic");
        AssertEx.Equal("web|127.0.0.1|18080|https", directListenerIdentityResponse.Key);
        AssertEx.Equal("127.0.0.1|18080|https", directListenerIdentityResponse.BindKey);
        AssertEx.Equal("web|127.0.0.1|18080|udp|quic", directQuicIdentityResponse.Key);
        AssertEx.Equal("127.0.0.1|18080|udp|quic", directQuicIdentityResponse.BindKey);
        AssertEx.Throws<ArgumentNullException>(() => RuntimeRouteResponseMapper.FromRoutes(null!));
        var routeResponses = RuntimeRouteResponseMapper.FromRoutes(projection.Routes.Select(static route => route));
        AssertEx.False(routeResponses is RuntimeRouteResponse[], "Configuration API routes should not expose a mutable array.");
        AssertEx.False(routeResponses[0].Upstreams is RuntimeUpstreamResponse[], "Configuration API route upstreams should not expose a mutable array.");
        AssertEx.False(routeResponses[0].Upstreams[0].CircuitBreaker.FailureStatusCodes is int[], "Configuration API circuit breaker status codes should not expose a mutable array.");
        var failureStatusCodes = new List<int>
        {
            503
        };
        var directCircuitBreakerResponse = new RuntimeCircuitBreakerResponse(enabled: true, failureThreshold: 3, samplingWindow: TimeSpan.FromSeconds(30), openDuration: TimeSpan.FromSeconds(10), halfOpenMaxAttempts: 1, failureStatusCodes: failureStatusCodes);
        var directUpstreamResponse = new RuntimeUpstreamResponse(routeName: "home", name: "local", scheme: "http", protocol: "http1", address: "127.0.0.1", port: 5000, weight: 1, tls: new RuntimeUpstreamTlsResponse(false, null), endpoint: "127.0.0.1:5000", uriEndpoint: "http://127.0.0.1:5000", effectiveSniHost: "", identity: "home/local", circuitBreaker: directCircuitBreakerResponse);
        failureStatusCodes[0] = 502;
        failureStatusCodes.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCircuitBreakerResponse(enabled: true, failureThreshold: 3, samplingWindow: TimeSpan.FromSeconds(30), openDuration: TimeSpan.FromSeconds(10), halfOpenMaxAttempts: 1, failureStatusCodes: null!));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeUpstreamResponse(routeName: "home", name: "local", scheme: "http", protocol: "http1", address: "127.0.0.1", port: 5000, weight: 1, tls: null!, endpoint: "127.0.0.1:5000", uriEndpoint: "http://127.0.0.1:5000", effectiveSniHost: "", identity: "home/local", circuitBreaker: directCircuitBreakerResponse));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeUpstreamResponse(routeName: "home", name: "local", scheme: "http", protocol: "http1", address: "127.0.0.1", port: 5000, weight: 1, tls: new RuntimeUpstreamTlsResponse(false, null), endpoint: "127.0.0.1:5000", uriEndpoint: "http://127.0.0.1:5000", effectiveSniHost: "", identity: "home/local", circuitBreaker: null!));
        AssertEx.Equal(503, directCircuitBreakerResponse.FailureStatusCodes[0]);
        AssertEx.Equal(503, directUpstreamResponse.CircuitBreaker.FailureStatusCodes[0]);
        AssertEx.Equal("home/local", directUpstreamResponse.Identity);
        AssertEx.False(directCircuitBreakerResponse.FailureStatusCodes is int[], "Direct configuration API circuit breaker status codes should not expose a mutable array.");
        var directRouteUpstreams = new List<RuntimeUpstreamResponse>
        {
            directUpstreamResponse
        };
        var directRouteResponse = new RuntimeRouteResponse(name: "home", host: routeResponses[0].Host, pathPrefix: routeResponses[0].PathPrefix, action: routeResponses[0].Action, loadBalancingPolicy: routeResponses[0].LoadBalancingPolicy, healthCheck: routeResponses[0].HealthCheck, upstreams: directRouteUpstreams, httpsRedirect: routeResponses[0].HttpsRedirect, canonicalHost: routeResponses[0].CanonicalHost, headerPolicy: routeResponses[0].HeaderPolicy, pathRewrite: routeResponses[0].PathRewrite, redirect: routeResponses[0].Redirect, staticResponse: routeResponses[0].StaticResponse, maintenance: routeResponses[0].Maintenance, cache: routeResponses[0].Cache, resolvedOptions: routeResponses[0].ResolvedOptions, siteName: "home", retry: routeResponses[0].Retry);
        directRouteUpstreams[0] = routeResponses[0].Upstreams[0];
        directRouteUpstreams.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRouteResponse(name: "home", host: routeResponses[0].Host, pathPrefix: routeResponses[0].PathPrefix, action: routeResponses[0].Action, loadBalancingPolicy: routeResponses[0].LoadBalancingPolicy, healthCheck: routeResponses[0].HealthCheck, upstreams: null!, httpsRedirect: routeResponses[0].HttpsRedirect, canonicalHost: routeResponses[0].CanonicalHost, headerPolicy: routeResponses[0].HeaderPolicy, pathRewrite: routeResponses[0].PathRewrite, redirect: routeResponses[0].Redirect, staticResponse: routeResponses[0].StaticResponse, maintenance: routeResponses[0].Maintenance, cache: routeResponses[0].Cache, resolvedOptions: routeResponses[0].ResolvedOptions, siteName: "home", retry: routeResponses[0].Retry));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRouteResponse(name: "home", host: routeResponses[0].Host, pathPrefix: routeResponses[0].PathPrefix, action: routeResponses[0].Action, loadBalancingPolicy: routeResponses[0].LoadBalancingPolicy, healthCheck: routeResponses[0].HealthCheck, upstreams: [], httpsRedirect: routeResponses[0].HttpsRedirect, canonicalHost: routeResponses[0].CanonicalHost, headerPolicy: routeResponses[0].HeaderPolicy, pathRewrite: routeResponses[0].PathRewrite, redirect: routeResponses[0].Redirect, staticResponse: routeResponses[0].StaticResponse, maintenance: routeResponses[0].Maintenance, cache: routeResponses[0].Cache, resolvedOptions: routeResponses[0].ResolvedOptions, siteName: "home", retry: null!));
        AssertEx.Equal("local", directRouteResponse.Upstreams[0].Name);
        AssertEx.Equal("home", directRouteResponse.SiteName);
        AssertEx.False(directRouteResponse.Upstreams is RuntimeUpstreamResponse[], "Direct configuration API route upstreams should not expose a mutable array.");
    }

    public static void ConfigurationValidationResultNamesValidationOutcomes()
    {
        var attemptedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(3);
        var loadedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(2);
        var discovery = new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []);
        var valid = ProxyConfigurationValidationResult.Valid(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 4, lastSuccessfulLoadAtUtc: loadedAtUtc, wouldBeVersion: 5, sourceFiles: ["sites/home.json"], discovery: discovery);
        var invalid = ProxyConfigurationValidationResult.Invalid(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 4, lastSuccessfulLoadAtUtc: loadedAtUtc, wouldBeVersion: null, sourceFiles: ["sites/broken.json"], discovery: discovery, errors: ["parse failed"], fileErrors: [ProxyConfigurationFileError.ForPath("sites/broken.json", "parse failed")]);
        AssertEx.True(valid is ProxyConfigurationValidationResult.ValidResult);
        AssertEx.Equal(4, valid.ActiveVersion!.Value);
        AssertEx.Equal(loadedAtUtc, valid.LastSuccessfulLoadAtUtc!.Value);
        AssertEx.Equal(5, valid.WouldBeVersion!.Value);
        AssertEx.Equal("sites/home.json", valid.SourceFiles[0]);
        AssertEx.Equal(0, valid.Errors.Count);
        AssertEx.Equal(0, valid.FileErrors.Count);
        AssertEx.True(invalid is ProxyConfigurationValidationResult.InvalidResult);
        AssertEx.Equal(4, invalid.ActiveVersion!.Value);
        AssertEx.Equal(loadedAtUtc, invalid.LastSuccessfulLoadAtUtc!.Value);
        AssertEx.Equal<int?>(null, invalid.WouldBeVersion);
        AssertEx.Equal("parse failed", invalid.Errors[0]);
        AssertEx.Equal("sites/broken.json", invalid.FileErrors[0].Path);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyConfigurationValidationResult.Valid(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 0, lastSuccessfulLoadAtUtc: loadedAtUtc, wouldBeVersion: 5, sourceFiles: ["sites/home.json"], discovery: discovery));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyConfigurationValidationResult.Invalid(sourceDirectory: "data", attemptedAtUtc: attemptedAtUtc, activeVersion: 4, lastSuccessfulLoadAtUtc: loadedAtUtc, wouldBeVersion: -1, sourceFiles: ["sites/broken.json"], discovery: discovery, errors: ["parse failed"], fileErrors: [ProxyConfigurationFileError.ForPath("sites/broken.json", "parse failed")]));
    }

    public static void ConfigurationReadResultNamesAvailableAndMissingOutcomes()
    {
        var projection = new TestConfigurationProjection("current");
        var available = ProxyConfigurationReadResult<TestConfigurationProjection>.Available(projection);
        var missing = ProxyConfigurationReadResult<TestConfigurationProjection>.MissingConfiguration;
        AssertEx.True(available is ProxyConfigurationReadResult<TestConfigurationProjection>.AvailableResult);
        AssertEx.Equal(projection, ((ProxyConfigurationReadResult<TestConfigurationProjection>.AvailableResult)available).Configuration);
        AssertEx.True(missing is not ProxyConfigurationReadResult<TestConfigurationProjection>.AvailableResult);
    }

    public static async Task ConfigurationLoadResultNamesLoadedValidatedAndFailedOutcomesAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var load = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(load);
        var loaded = ProxyConfigurationLoadResult.Loaded(load.SourceDirectory, snapshot, load.Discovery);
        var validated = ProxyConfigurationLoadResult.Validated(load.SourceDirectory, DateTimeOffset.UnixEpoch, load.SourceFiles, load.Discovery, wouldBeVersion: snapshot.Version + 1);
        var failed = ProxyConfigurationLoadResult.Failed(load.SourceDirectory, DateTimeOffset.UnixEpoch, load.SourceFiles, load.Discovery, [ProxyConfigurationFileError.ForPath("sites/broken.json", "parse failed")], wouldBeVersion: null);
        AssertEx.True(loaded is ProxyConfigurationLoadResult.LoadedResult);
        var loadedResult = (ProxyConfigurationLoadResult.LoadedResult)loaded;
        AssertEx.Equal(snapshot, loadedResult.Snapshot);
        AssertEx.Equal(snapshot.Version, loadedResult.WouldBeVersion!.Value);
        AssertEx.Equal(0, loadedResult.Errors.Count);
        AssertEx.True(validated is ProxyConfigurationLoadResult.ValidatedResult);
        var validatedResult = (ProxyConfigurationLoadResult.ValidatedResult)validated;
        AssertEx.Equal(snapshot.Version + 1, validatedResult.WouldBeVersion!.Value);
        AssertEx.Equal(0, validatedResult.Errors.Count);
        AssertEx.True(failed is ProxyConfigurationLoadResult.FailedResult);
        var failedResult = (ProxyConfigurationLoadResult.FailedResult)failed;
        AssertEx.Equal<int?>(null, failedResult.WouldBeVersion);
        AssertEx.Equal("sites/broken.json: parse failed", failedResult.Errors[0]);
        AssertEx.Equal("sites/broken.json", failedResult.FileErrors[0].Path);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyConfigurationLoadResult.Validated(load.SourceDirectory, DateTimeOffset.UnixEpoch, load.SourceFiles, load.Discovery, wouldBeVersion: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyConfigurationLoadResult.Failed(load.SourceDirectory, DateTimeOffset.UnixEpoch, load.SourceFiles, load.Discovery, [ProxyConfigurationFileError.ForPath("sites/broken.json", "parse failed")], wouldBeVersion: -1));
    }

    public static void DataDirectoryUsesConfiguredOverride()
    {
        var expected = Path.Combine(Path.GetTempPath(), $"mdrava-test-{Guid.NewGuid():N}");
        var provider = new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = expected });
        AssertEx.Equal(Path.GetFullPath(expected), provider.GetDataDirectory());
        AssertEx.Equal(Path.Combine(Path.GetFullPath(expected), "config"), provider.GetProxyConfigDirectory());
        AssertEx.Equal(Path.Combine(Path.GetFullPath(expected), "config", "sites"), provider.GetSitesConfigDirectory());
        AssertEx.Equal(Path.Combine(Path.GetFullPath(expected), "logs"), provider.GetLogsDirectory());
        AssertEx.Equal(Path.Combine(Path.GetFullPath(expected), "certs"), provider.GetCertificatesDirectory());
        AssertEx.Equal(Path.Combine(Path.GetFullPath(expected), "state"), provider.GetStateDirectory());
    }

    public static void DataDirectoryUsesEnvironmentOverride()
    {
        var previous = Environment.GetEnvironmentVariable(MdravaDataDirectoryProvider.EnvironmentVariableName);
        var expected = Path.Combine(Path.GetTempPath(), $"mdrava-env-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable(MdravaDataDirectoryProvider.EnvironmentVariableName, expected);
            var provider = new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = Path.Combine(Path.GetTempPath(), "ignored") });
            AssertEx.Equal(Path.GetFullPath(expected), provider.GetDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable(MdravaDataDirectoryProvider.EnvironmentVariableName, previous);
        }
    }

    public static void DataDirectoryDefaultsUnderLocalApplicationDataWhenAvailable()
    {
        var previous = Environment.GetEnvironmentVariable(MdravaDataDirectoryProvider.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(MdravaDataDirectoryProvider.EnvironmentVariableName, null);
            var provider = new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions());
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                AssertEx.Equal(Path.Combine(localAppData, "MDRAVA"), provider.GetDataDirectory());
            }
            else
            {
                AssertEx.Equal(Path.Combine(AppContext.BaseDirectory, "MDRAVA"), provider.GetDataDirectory());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(MdravaDataDirectoryProvider.EnvironmentVariableName, previous);
        }
    }

    public static async Task LoaderLoadsValidSiteFilesAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var loadedAtUtc = new DateTimeOffset(2026, 6, 10, 11, 15, 0, TimeSpan.Zero);
        var loader = CreateLoader(temp.Path, new FixedTimeProvider(loadedAtUtc));
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        AssertEx.Equal(loadedAtUtc, result.AttemptedAtUtc);
        AssertEx.Equal(loadedAtUtc, snapshot.LoadedAtUtc);
        AssertEx.Equal(Path.Combine(temp.Path, "config", "sites"), result.SourceDirectory);
        AssertEx.Equal(1, snapshot.Listeners.Count);
        AssertEx.Equal(1, snapshot.Routes.Count);
        AssertEx.Equal("home", snapshot.Routes[0].Name);
        AssertEx.Equal(1, snapshot.SourceFiles.Count);
        AssertEx.Equal(TimeSpan.FromSeconds(10), snapshot.Timeouts.ClientRequestHeadTimeout);
    }

    public static async Task LoaderLoadsEquivalentJsonAndYamlSiteFilesAsync()
    {
        using var jsonTemp = TemporaryDirectory.Create();
        using var yamlTemp = TemporaryDirectory.Create();
        WriteSite(jsonTemp.Path, "home.json", port: 18080, upstreamPort: 15000);
        WriteYamlSite(yamlTemp.Path, "home.yml", port: 18080, upstreamPort: 15000);
        var jsonResult = await CreateLoader(jsonTemp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var yamlResult = await CreateLoader(yamlTemp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var jsonSnapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(jsonResult);
        var yamlSnapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(yamlResult);
        AssertEx.Equal(jsonSnapshot.Listeners[0].Name, yamlSnapshot.Listeners[0].Name);
        AssertEx.Equal(jsonSnapshot.Listeners[0].Port, yamlSnapshot.Listeners[0].Port);
        AssertEx.Equal(jsonSnapshot.Routes[0].Name, yamlSnapshot.Routes[0].Name);
        AssertEx.Equal(jsonSnapshot.Routes[0].Upstreams[0].Endpoint, yamlSnapshot.Routes[0].Upstreams[0].Endpoint);
        AssertEx.True(yamlSnapshot.Discovery.Files.Any(static file => string.Equals(file.Format, "yaml", StringComparison.Ordinal) && string.Equals(file.Status, "loaded", StringComparison.Ordinal)));
    }

    public static async Task LoaderReportsYamlParseErrorsWithPerFileDiagnosticsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var sites = Directory.CreateDirectory(Path.Combine(temp.Path, "config", "sites")).FullName;
        var yamlPath = Path.Combine(sites, "broken.yaml");
        await File.WriteAllTextAsync(yamlPath, "name: broken\nlisteners:\n  - name: main\n    port: [not-closed\n").ConfigureAwait(false);
        var attemptedAtUtc = new DateTimeOffset(2026, 6, 10, 11, 20, 0, TimeSpan.Zero);
        var loader = CreateLoader(temp.Path, new FixedTimeProvider(attemptedAtUtc));
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.Equal(attemptedAtUtc, result.AttemptedAtUtc);
        AssertEx.True(result.FileErrors.Any(error => string.Equals(error.Path, yamlPath, StringComparison.OrdinalIgnoreCase)));
        AssertEx.True(result.Errors.Any(static error => error.Contains("YAML", StringComparison.OrdinalIgnoreCase)), string.Join("; ", result.Errors));
        AssertEx.True(result.Discovery.Files.Any(file => string.Equals(file.Path, yamlPath, StringComparison.OrdinalIgnoreCase) && string.Equals(file.Format, "yaml", StringComparison.Ordinal) && string.Equals(file.Status, "failed", StringComparison.Ordinal)));
    }

    public static async Task LoaderLoadsRouteLoadBalancingAndHealthCheckSettingsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSiteWithTwoUpstreams(temp.Path, "pool.json", port: 18080, firstUpstreamPort: 15000, secondUpstreamPort: 15001, healthCheckEnabled: true);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var route = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Routes[0];
        AssertEx.Equal("round-robin", route.LoadBalancingPolicy);
        AssertEx.True(route.HealthCheck.Enabled);
        AssertEx.Equal(2, route.Upstreams.Count);
        AssertEx.Equal(2, route.Upstreams[1].Weight);
    }

    public static async Task LoaderCreatesMissingConfigDirectoriesAndLoadsEmptySnapshotAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var configDirectory = Path.Combine(temp.Path, "config");
        var sitesDirectory = Path.Combine(configDirectory, "sites");
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        AssertEx.True(Directory.Exists(configDirectory));
        AssertEx.True(Directory.Exists(sitesDirectory));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "logs")));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "certs")));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "state")));
        AssertEx.True(File.Exists(Path.Combine(configDirectory, "proxy.json")));
        AssertEx.True(File.Exists(Path.Combine(sitesDirectory, "example.site.yaml")));
        AssertEx.Equal(0, snapshot.Listeners.Count);
        AssertEx.Equal(0, snapshot.Routes.Count);
        AssertEx.Equal(0, snapshot.SourceFiles.Count);
        AssertEx.True(snapshot.Discovery.CreatedPaths.Count > 0);
        AssertEx.True(snapshot.Discovery.Files.Any(static file => string.Equals(file.Status, "skipped", StringComparison.Ordinal) && string.Equals(file.Format, "yaml", StringComparison.Ordinal)));
    }

    public static async Task LoaderDoesNotOverwriteExistingPlaceholderFilesAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var config = Directory.CreateDirectory(Path.Combine(temp.Path, "config")).FullName;
        var sites = Directory.CreateDirectory(Path.Combine(config, "sites")).FullName;
        var proxyPath = Path.Combine(config, "proxy.json");
        var examplePath = Path.Combine(sites, "example.site.yaml");
        await File.WriteAllTextAsync(proxyPath, "{ \"observability\": { \"accessLogEnabled\": false } }").ConfigureAwait(false);
        await File.WriteAllTextAsync(examplePath, "# custom example").ConfigureAwait(false);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        AssertEx.Equal("{ \"observability\": { \"accessLogEnabled\": false } }", (await File.ReadAllTextAsync(proxyPath).ConfigureAwait(false)));
        AssertEx.Equal("# custom example", (await File.ReadAllTextAsync(examplePath).ConfigureAwait(false)));
        AssertEx.False(snapshot.Observability.AccessLogEnabled);
    }

    public static async Task LoaderLoadsExistingEmptySitesDirectoryAsync()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "config", "sites"));
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        AssertEx.Equal(0, snapshot.Listeners.Count);
        AssertEx.Equal(0, snapshot.Routes.Count);
    }

    public static async Task LoaderUsesDefaultsWhenOperationalConfigIsMissingAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        AssertEx.True(File.Exists(Path.Combine(temp.Path, "config", "proxy.json")));
        AssertEx.Equal(TimeSpan.FromSeconds(10), snapshot.Timeouts.ClientRequestHeadTimeout);
    }

    public static async Task LoaderLoadsExplicitOperationalTimeoutsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        WriteOperationalConfig(temp.Path, clientRequestHeadTimeoutMs: 250, tunnelIdleTimeoutMs: 750);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        AssertEx.Equal(TimeSpan.FromMilliseconds(250), snapshot.Timeouts.ClientRequestHeadTimeout);
        AssertEx.Equal(TimeSpan.FromMilliseconds(750), snapshot.Timeouts.TunnelIdleTimeout);
    }

    public static async Task LoaderLoadsObservabilityDefaultsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var observability = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Observability;
        AssertEx.True(observability.AccessLogEnabled);
        AssertEx.Equal(500, observability.RecentDiagnosticsCapacity);
        AssertEx.True(observability.LogPersistence.AccessLogEnabled);
        AssertEx.True(observability.LogPersistence.AdminAuditEnabled);
        AssertEx.Equal(1_048_576L, observability.LogPersistence.MaxFileBytes);
        AssertEx.Equal(8, observability.LogPersistence.MaxFiles);
    }

    public static async Task LoaderLoadsExplicitObservabilitySettingsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        WriteOperationalConfig(temp.Path, accessLogEnabled: false, recentDiagnosticsCapacity: 12, accessLogFileEnabled: false, adminAuditLogFileEnabled: false, logMaxFileBytes: 8192, logMaxFiles: 3);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var observability = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Observability;
        AssertEx.False(observability.AccessLogEnabled);
        AssertEx.Equal(12, observability.RecentDiagnosticsCapacity);
        AssertEx.False(observability.LogPersistence.AccessLogEnabled);
        AssertEx.False(observability.LogPersistence.AdminAuditEnabled);
        AssertEx.Equal(8192L, observability.LogPersistence.MaxFileBytes);
        AssertEx.Equal(3, observability.LogPersistence.MaxFiles);
    }

    public static async Task LoaderRejectsInvalidObservabilityCapacityAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        WriteOperationalConfig(temp.Path, recentDiagnosticsCapacity: 0);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("RecentDiagnosticsCapacity", StringComparison.Ordinal)));
    }

    public static async Task LoaderRejectsInvalidLogPersistenceSettingsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        WriteOperationalConfig(temp.Path, logMaxFileBytes: 1024, logMaxFiles: 0);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("MaxFileBytes", StringComparison.Ordinal)), string.Join("; ", result.Errors));
        AssertEx.True(result.Errors.Any(static error => error.Contains("MaxFiles", StringComparison.Ordinal)), string.Join("; ", result.Errors));
    }

    public static async Task LoaderLoadsLimitDefaultsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var limits = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Limits;
        AssertEx.Equal(4096, limits.MaxActiveClientConnections);
        AssertEx.Equal(128, limits.MaxConcurrentTlsHandshakes);
        AssertEx.Equal(240, limits.RequestsPerMinutePerIp);
        AssertEx.Equal(TimeSpan.FromSeconds(15), limits.ShutdownGracePeriod);
    }

    public static async Task LoaderRejectsInvalidLimitSettingsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        WriteOperationalConfig(temp.Path, maxActiveClientConnections: 0);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("MaxActiveClientConnections", StringComparison.Ordinal)));
    }

    public static async Task LoaderRejectsInvalidOperationalTimeoutsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        WriteOperationalConfig(temp.Path, clientRequestHeadTimeoutMs: 1);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Count > 0);
    }

    public static async Task LoaderRejectsInvalidTunnelLimitAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        WriteOperationalConfig(temp.Path, maxActiveUpgradedTunnels: 0);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("MaxActiveUpgradedTunnels", StringComparison.Ordinal)));
    }

    public static async Task LoaderLoadsHttpsListenerWithCertificateAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var certificatePath = Path.Combine(temp.Path, "certs", "home.pfx");
        TestCertificates.WriteSelfSignedPfx(certificatePath, "home.test", "secret");
        WriteHttpsSite(temp.Path, "home.json", port: 18443, upstreamPort: 15000, certificateId: "home-cert");
        WriteOperationalConfig(temp.Path, certificateId: "home-cert", certificatePath: "certs/home.pfx", certificatePassword: "secret");
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        AssertEx.Equal(RuntimeListenerTransport.Https, snapshot.Listeners[0].Transport);
        AssertEx.Equal(1, snapshot.Certificates.Count);
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot));
        AssertEx.Equal(1, projection.Certificates.Count);
        AssertEx.Equal(true, projection.Certificates[0].HasConfiguredPassword);
    }

    public static async Task LoaderRejectsHttpsListenerWithMissingCertificateReferenceAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteHttpsSite(temp.Path, "home.json", port: 18443, upstreamPort: 15000, certificateId: "missing-cert");
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("unknown certificate", StringComparison.OrdinalIgnoreCase)));
    }

    public static async Task LoaderRejectsInvalidCertificatePathAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteHttpsSite(temp.Path, "home.json", port: 18443, upstreamPort: 15000, certificateId: "home-cert");
        WriteOperationalConfig(temp.Path, certificateId: "home-cert", certificatePath: "certs/missing.pfx");
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("file does not exist", StringComparison.OrdinalIgnoreCase)));
    }

    public static async Task LoaderRejectsInvalidCertificatePasswordAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var certificatePath = Path.Combine(temp.Path, "certs", "home.pfx");
        TestCertificates.WriteSelfSignedPfx(certificatePath, "home.test", "correct");
        WriteHttpsSite(temp.Path, "home.json", port: 18443, upstreamPort: 15000, certificateId: "home-cert");
        WriteOperationalConfig(temp.Path, certificateId: "home-cert", certificatePath: "certs/home.pfx", certificatePassword: "wrong");
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("could not be loaded", StringComparison.OrdinalIgnoreCase)));
    }

    public static async Task LoaderRejectsDuplicateSniCertificateMappingAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var certificatePath = Path.Combine(temp.Path, "certs", "home.pfx");
        TestCertificates.WriteSelfSignedPfx(certificatePath, "home.test");
        WriteHttpsSite(temp.Path, "home.json", port: 18443, upstreamPort: 15000, certificateId: "home-cert", duplicateSni: true);
        WriteOperationalConfig(temp.Path, certificateId: "home-cert", certificatePath: "certs/home.pfx");
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("duplicated", StringComparison.OrdinalIgnoreCase)));
    }

    public static async Task LoaderMergesSniMappingsFromSharedHttpsListenerAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var certificatePath = Path.Combine(temp.Path, "certs", "home.pfx");
        TestCertificates.WriteSelfSignedPfx(certificatePath, "home.test");
        WriteHttpsSite(temp.Path, "home.json", port: 18443, upstreamPort: 15000, certificateId: "home-cert", sniHost: "home.test");
        WriteHttpsSite(temp.Path, "alt.json", port: 18443, upstreamPort: 15001, certificateId: "home-cert", sniHost: "alt.test");
        WriteOperationalConfig(temp.Path, certificateId: "home-cert", certificatePath: "certs/home.pfx");
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        AssertEx.Equal(1, snapshot.Listeners.Count);
        AssertEx.Equal(2, snapshot.Listeners[0].SniCertificates.Count);
        AssertEx.Equal(2, snapshot.Routes.Count);
    }

    public static async Task LoaderRejectsInvalidSiteFileAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var sites = Directory.CreateDirectory(Path.Combine(temp.Path, "config", "sites")).FullName;
        await File.WriteAllTextAsync(Path.Combine(sites, "broken.json"), "{ nope").ConfigureAwait(false);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Count > 0);
    }

    public static async Task ReloadPreservesActiveSnapshotWhenLoadFailsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        AssertEx.Equal(1, store.Snapshot.Version);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "sites", "broken.json"), "{ nope").ConfigureAwait(false);
        var second = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Failed(second);
        AssertEx.Equal(1, store.Snapshot.Version);
    }

    public static async Task ReloadReplacesActiveSnapshotWhenLoadSucceedsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "sites", "home.json"), SiteJson("home", 18081, 15001)).ConfigureAwait(false);
        var second = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(second, string.Join("; ", second.Errors));
        AssertEx.Equal(2, store.Snapshot.Version);
        AssertEx.Equal(18081, store.Snapshot.Listeners[0].Port);
    }

    public static async Task ReloadReplacesActiveSnapshotWithEmptySitesDirectoryAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        foreach (var siteFile in Directory.EnumerateFiles(Path.Combine(temp.Path, "config", "sites"), "*.json"))
        {
            File.Delete(siteFile);
        }

        var second = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(second, string.Join("; ", second.Errors));
        AssertEx.Equal(2, store.Snapshot.Version);
        AssertEx.Equal(0, store.Snapshot.Listeners.Count);
        AssertEx.Equal(0, store.Snapshot.Routes.Count);
    }

    public static async Task ActiveInspectionProjectionReflectsStoreAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var result = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        var reload = ProxyConfigurationReloadResultAssertions.Reloaded(result);
        var projection = reload.ActiveConfiguration;
        AssertEx.Equal(1, projection.Version);
        AssertEx.Equal("home", projection.Routes[0].Name);
        AssertEx.Equal(1, projection.SourceFiles.Count);
        VerifyInspectionUsesReadModels(projection);
    }

    private static void VerifyInspectionUsesReadModels(ProxyConfigurationProjection projection)
    {
        object acme = projection.Acme;
        AssertEx.True(acme is RuntimeAcmeProjection);
        AssertEx.False(acme is RuntimeAcmeOptions);
        object acmeCertificates = projection.Acme.Certificates;
        AssertEx.False(acmeCertificates is RuntimeAcmeCertificateOptions[]);
        object timeouts = projection.Timeouts;
        AssertEx.True(timeouts is RuntimeTimeoutsProjection);
        AssertEx.False(timeouts is RuntimeTimeouts);
        object connectionLimits = projection.ConnectionLimits;
        AssertEx.True(connectionLimits is RuntimeConnectionLimitsProjection);
        AssertEx.False(connectionLimits is RuntimeConnectionLimits);
        object observability = projection.Observability;
        AssertEx.True(observability is RuntimeObservabilityProjection);
        AssertEx.False(observability is RuntimeObservabilityOptions);
        object logPersistence = projection.Observability.LogPersistence;
        AssertEx.True(logPersistence is RuntimeLogPersistenceProjection);
        AssertEx.False(logPersistence is RuntimeLogPersistenceOptions);
        object limits = projection.Limits;
        AssertEx.True(limits is RuntimeLimitsProjection);
        AssertEx.False(limits is RuntimeLimits);
        object forwardedHeaders = projection.ForwardedHeaders;
        AssertEx.True(forwardedHeaders is RuntimeForwardedHeadersProjection);
        AssertEx.False(forwardedHeaders is RuntimeForwardedHeadersOptions);
        object metrics = projection.Metrics;
        AssertEx.True(metrics is RuntimeMetricsProjection);
        AssertEx.False(metrics is RuntimeMetricsOptions);
        AssertRuntimeMetricsOptionsRejects(endpointPath: null!);
        AssertRuntimeMetricsOptionsRejects(endpointPath: " ");
        AssertRuntimeMetricsOptionsRejects(endpointPath: "/metrics");
        AssertRuntimeMetricsProjectionRejects(endpointPath: null!);
        AssertRuntimeMetricsProjectionRejects(endpointPath: " ");
        AssertRuntimeMetricsProjectionRejects(endpointPath: "/metrics");
    }

    public static async Task ActiveInspectionProjectionUsesListenerReadModelsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var result = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        var projection = ProxyConfigurationReloadResultAssertions.Reloaded(result).ActiveConfiguration;
        var listener = projection.Listeners[0];
        object listenerCollection = projection.Listeners;
        AssertEx.Equal("main", listener.Name);
        AssertEx.Equal(18080, listener.Port);
        AssertEx.Equal(RuntimeListenerTransport.Http, listener.Transport);
        AssertEx.Equal(listener.Name, listener.Identity.Name);
        AssertEx.Equal(listener.Address, listener.Identity.Address);
        AssertEx.Equal(listener.Port, listener.Identity.Port);
        object identity = listener.Identity;
        AssertEx.True(identity is RuntimeListenerIdentityProjection);
        AssertEx.False(identity is RuntimeListenerIdentity);
        object sniCertificates = listener.SniCertificates;
        AssertEx.True(sniCertificates is IReadOnlyList<RuntimeSniCertificateBindingProjection>);
        AssertEx.False(sniCertificates is IReadOnlyList<RuntimeSniCertificateBinding>);
        AssertEx.False(sniCertificates is RuntimeSniCertificateBinding[]);
        AssertEx.False(sniCertificates is RuntimeSniCertificateBindingProjection[]);
        object http3AltSvc = listener.Http3AltSvc;
        AssertEx.True(http3AltSvc is RuntimeHttp3AltSvcProjection);
        AssertEx.False(http3AltSvc is RuntimeHttp3AltSvcOptions);
        object http2Limits = listener.Http2Limits;
        AssertEx.True(http2Limits is RuntimeHttp2LimitsProjection);
        AssertEx.False(http2Limits is RuntimeHttp2Limits);
        AssertHttp3AltSvcOptionsRejects(maxAgeSeconds: -1);
        AssertHttp3AltSvcOptionsRejects(maxAgeSeconds: 31536001);
        AssertHttp3AltSvcProjectionRejects(maxAgeSeconds: -1);
        AssertHttp3AltSvcProjectionRejects(maxAgeSeconds: 31536001);
        AssertHttp3ListenerReadinessRejects(enablementLevel: null!);
        AssertHttp3ListenerReadinessRejects(enablementLevel: " ");
        AssertHttp3ListenerReadinessRejects(disabledReason: null!);
        AssertHttp3ListenerReadinessRejects(disabledReason: " ");
        AssertHttp3ListenerReadinessRejects(altSvcMaxAgeSeconds: -1);
        AssertHttp3ListenerReadinessRejects(altSvcMaxAgeSeconds: 31536001);
        AssertHttp3ListenerReadinessProjectionRejects(enablementLevel: null!);
        AssertHttp3ListenerReadinessProjectionRejects(enablementLevel: " ");
        AssertHttp3ListenerReadinessProjectionRejects(disabledReason: null!);
        AssertHttp3ListenerReadinessProjectionRejects(disabledReason: " ");
        AssertHttp3ListenerReadinessProjectionRejects(altSvcMaxAgeSeconds: -1);
        AssertHttp3ListenerReadinessProjectionRejects(altSvcMaxAgeSeconds: 31536001);
        AssertHttp2LimitsReject(maxConcurrentStreams: 0);
        AssertHttp2LimitsReject(maxConcurrentStreams: 1001);
        AssertHttp2LimitsReject(maxHeaderListBytes: 1023);
        AssertHttp2LimitsReject(maxHeaderListBytes: 1048577);
        AssertHttp2LimitsReject(maxFrameSize: 16383);
        AssertHttp2LimitsReject(maxFrameSize: 16777216);
        AssertHttp2LimitsProjectionReject(maxConcurrentStreams: 0);
        AssertHttp2LimitsProjectionReject(maxConcurrentStreams: 1001);
        AssertHttp2LimitsProjectionReject(maxHeaderListBytes: 1023);
        AssertHttp2LimitsProjectionReject(maxHeaderListBytes: 1048577);
        AssertHttp2LimitsProjectionReject(maxFrameSize: 16383);
        AssertHttp2LimitsProjectionReject(maxFrameSize: 16777216);
        AssertRuntimeListenerRejects(port: 0);
        AssertRuntimeListenerRejects(port: 65536);
        AssertRuntimeListenerRejects(backlog: 0);
        AssertRuntimeListenerRejects(maxRequestHeadBytes: 1023);
        AssertRuntimeListenerRejects(maxRequestHeadBytes: 1048577);
        AssertRuntimeListenerRejects(maxResponseHeadBytes: 1023);
        AssertRuntimeListenerRejects(maxResponseHeadBytes: 1048577);
        AssertRuntimeListenerRejects(maxChunkLineBytes: 63);
        AssertRuntimeListenerRejects(maxChunkLineBytes: 16385);
        AssertRuntimeListenerRejects(forwardingBufferBytes: 4095);
        AssertRuntimeListenerRejects(forwardingBufferBytes: 1048577);
        AssertRuntimeListenerProjectionRejects(port: 0);
        AssertRuntimeListenerProjectionRejects(port: 65536);
        AssertRuntimeListenerProjectionRejects(backlog: 0);
        AssertRuntimeListenerProjectionRejects(maxRequestHeadBytes: 1023);
        AssertRuntimeListenerProjectionRejects(maxRequestHeadBytes: 1048577);
        AssertRuntimeListenerProjectionRejects(maxResponseHeadBytes: 1023);
        AssertRuntimeListenerProjectionRejects(maxResponseHeadBytes: 1048577);
        AssertRuntimeListenerProjectionRejects(maxChunkLineBytes: 63);
        AssertRuntimeListenerProjectionRejects(maxChunkLineBytes: 16385);
        AssertRuntimeListenerProjectionRejects(forwardingBufferBytes: 4095);
        AssertRuntimeListenerProjectionRejects(forwardingBufferBytes: 1048577);
        AssertRuntimeListenerRejects(name: null!);
        AssertRuntimeListenerRejects(name: " ");
        AssertRuntimeListenerRejects(address: null!);
        AssertRuntimeListenerRejects(address: " ");
        AssertRuntimeListenerRejects(defaultCertificateId: " ");
        AssertRuntimeListenerRejects(transport: (RuntimeListenerTransport)99);
        AssertRuntimeListenerIdentityRejects(name: null!);
        AssertRuntimeListenerIdentityRejects(name: " ");
        AssertRuntimeListenerIdentityRejects(address: null!);
        AssertRuntimeListenerIdentityRejects(address: " ");
        AssertRuntimeListenerIdentityRejects(port: 0);
        AssertRuntimeListenerIdentityRejects(port: 65536);
        AssertRuntimeListenerIdentityRejects(transport: (RuntimeListenerTransport)99);
        AssertRuntimeListenerIdentityProjectionRejects(name: null!);
        AssertRuntimeListenerIdentityProjectionRejects(name: " ");
        AssertRuntimeListenerIdentityProjectionRejects(address: null!);
        AssertRuntimeListenerIdentityProjectionRejects(address: " ");
        AssertRuntimeListenerIdentityProjectionRejects(port: 0);
        AssertRuntimeListenerIdentityProjectionRejects(port: 65536);
        AssertRuntimeListenerIdentityProjectionRejects(transport: (RuntimeListenerTransport)99);
        AssertRuntimeListenerIdentityProjectionRejects(key: null!);
        AssertRuntimeListenerIdentityProjectionRejects(key: " ");
        AssertRuntimeListenerIdentityProjectionRejects(bindKey: null!);
        AssertRuntimeListenerIdentityProjectionRejects(bindKey: " ");
        AssertRuntimeListenerProjectionRejects(name: null!);
        AssertRuntimeListenerProjectionRejects(name: " ");
        AssertRuntimeListenerProjectionRejects(address: null!);
        AssertRuntimeListenerProjectionRejects(address: " ");
        AssertRuntimeListenerProjectionRejects(defaultCertificateId: " ");
        AssertRuntimeListenerProjectionRejects(transport: (RuntimeListenerTransport)99);
        AssertRuntimeQuicListenerIdentityRejects(name: null!);
        AssertRuntimeQuicListenerIdentityRejects(name: " ");
        AssertRuntimeQuicListenerIdentityRejects(address: null!);
        AssertRuntimeQuicListenerIdentityRejects(address: " ");
        AssertRuntimeQuicListenerIdentityRejects(port: 0);
        AssertRuntimeQuicListenerIdentityRejects(port: 65536);
        AssertRuntimeQuicListenerIdentityProjectionRejects(name: null!);
        AssertRuntimeQuicListenerIdentityProjectionRejects(name: " ");
        AssertRuntimeQuicListenerIdentityProjectionRejects(address: null!);
        AssertRuntimeQuicListenerIdentityProjectionRejects(address: " ");
        AssertRuntimeQuicListenerIdentityProjectionRejects(port: 0);
        AssertRuntimeQuicListenerIdentityProjectionRejects(port: 65536);
        AssertRuntimeQuicListenerIdentityProjectionRejects(key: null!);
        AssertRuntimeQuicListenerIdentityProjectionRejects(key: " ");
        AssertRuntimeQuicListenerIdentityProjectionRejects(bindKey: null!);
        AssertRuntimeQuicListenerIdentityProjectionRejects(bindKey: " ");
        object http3 = listener.Http3;
        AssertEx.True(http3 is RuntimeHttp3ListenerReadinessProjection);
        AssertEx.False(http3 is RuntimeHttp3ListenerReadiness);
        object? quicIdentity = listener.QuicIdentity;
        AssertEx.True(quicIdentity is null or RuntimeQuicListenerIdentityProjection);
        AssertEx.False(quicIdentity is RuntimeQuicListenerIdentity);
        AssertEx.False(listenerCollection is RuntimeListener[]);
        AssertEx.False(listenerCollection is RuntimeListenerProjection[]);
        var directSniCertificates = new List<RuntimeSniCertificateBindingProjection>
        {
            new("owned.example.test", "owned-cert")
        };
        var directListener = new RuntimeListenerProjection(Name: "main", Address: listener.Address, Port: listener.Port, Enabled: listener.Enabled, Transport: listener.Transport, DefaultCertificateId: listener.DefaultCertificateId, SniCertificates: directSniCertificates, Backlog: listener.Backlog, MaxRequestHeadBytes: listener.MaxRequestHeadBytes, MaxResponseHeadBytes: listener.MaxResponseHeadBytes, MaxChunkLineBytes: listener.MaxChunkLineBytes, ForwardingBufferBytes: listener.ForwardingBufferBytes, Identity: listener.Identity, Protocols: listener.Protocols, Http3Enablement: listener.Http3Enablement, Http3AltSvc: listener.Http3AltSvc, Http2Limits: listener.Http2Limits, TcpTrafficEnabled: listener.TcpTrafficEnabled, Http3ProtocolConfigured: listener.Http3ProtocolConfigured, QuicIdentity: listener.QuicIdentity, Http3: listener.Http3);
        directSniCertificates[0] = new RuntimeSniCertificateBindingProjection("replacement.test", "replacement-cert");
        directSniCertificates.Clear();
        AssertRuntimeSniCertificateBindingRejects(hostName: null!);
        AssertRuntimeSniCertificateBindingRejects(hostName: " ");
        AssertRuntimeSniCertificateBindingRejects(certificateId: null!);
        AssertRuntimeSniCertificateBindingRejects(certificateId: " ");
        AssertRuntimeSniCertificateBindingProjectionRejects(hostName: null!);
        AssertRuntimeSniCertificateBindingProjectionRejects(hostName: " ");
        AssertRuntimeSniCertificateBindingProjectionRejects(certificateId: null!);
        AssertRuntimeSniCertificateBindingProjectionRejects(certificateId: " ");
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeListenerProjection(Name: "main", Address: listener.Address, Port: listener.Port, Enabled: listener.Enabled, Transport: listener.Transport, DefaultCertificateId: listener.DefaultCertificateId, SniCertificates: null!, Backlog: listener.Backlog, MaxRequestHeadBytes: listener.MaxRequestHeadBytes, MaxResponseHeadBytes: listener.MaxResponseHeadBytes, MaxChunkLineBytes: listener.MaxChunkLineBytes, ForwardingBufferBytes: listener.ForwardingBufferBytes, Identity: listener.Identity, Protocols: listener.Protocols, Http3Enablement: listener.Http3Enablement, Http3AltSvc: listener.Http3AltSvc, Http2Limits: listener.Http2Limits, TcpTrafficEnabled: listener.TcpTrafficEnabled, Http3ProtocolConfigured: listener.Http3ProtocolConfigured, QuicIdentity: listener.QuicIdentity, Http3: listener.Http3));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeListenerProjection(Name: "main", Address: listener.Address, Port: listener.Port, Enabled: listener.Enabled, Transport: listener.Transport, DefaultCertificateId: listener.DefaultCertificateId, SniCertificates: [], Backlog: listener.Backlog, MaxRequestHeadBytes: listener.MaxRequestHeadBytes, MaxResponseHeadBytes: listener.MaxResponseHeadBytes, MaxChunkLineBytes: listener.MaxChunkLineBytes, ForwardingBufferBytes: listener.ForwardingBufferBytes, Identity: null!, Protocols: listener.Protocols, Http3Enablement: listener.Http3Enablement, Http3AltSvc: listener.Http3AltSvc, Http2Limits: listener.Http2Limits, TcpTrafficEnabled: listener.TcpTrafficEnabled, Http3ProtocolConfigured: listener.Http3ProtocolConfigured, QuicIdentity: listener.QuicIdentity, Http3: listener.Http3));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeListenerProjection(Name: "main", Address: listener.Address, Port: listener.Port, Enabled: listener.Enabled, Transport: listener.Transport, DefaultCertificateId: listener.DefaultCertificateId, SniCertificates: [], Backlog: listener.Backlog, MaxRequestHeadBytes: listener.MaxRequestHeadBytes, MaxResponseHeadBytes: listener.MaxResponseHeadBytes, MaxChunkLineBytes: listener.MaxChunkLineBytes, ForwardingBufferBytes: listener.ForwardingBufferBytes, Identity: listener.Identity, Protocols: listener.Protocols, Http3Enablement: listener.Http3Enablement, Http3AltSvc: listener.Http3AltSvc, Http2Limits: listener.Http2Limits, TcpTrafficEnabled: listener.TcpTrafficEnabled, Http3ProtocolConfigured: listener.Http3ProtocolConfigured, QuicIdentity: listener.QuicIdentity, Http3: null!));
        AssertEx.Equal("owned.example.test", directListener.SniCertificates[0].HostName);
        AssertEx.Equal("main", directListener.Name);
        AssertEx.False(directListener.SniCertificates is RuntimeSniCertificateBindingProjection[]);
        static void AssertHttp3AltSvcOptionsRejects(int maxAgeSeconds)
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeHttp3AltSvcOptions(Enabled: true, maxAgeSeconds));
        }

        static void AssertHttp3AltSvcProjectionRejects(int maxAgeSeconds)
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeHttp3AltSvcProjection(Enabled: true, maxAgeSeconds));
        }

        static void AssertHttp3ListenerReadinessRejects(string enablementLevel = "default", string disabledReason = "default_enabled", int altSvcMaxAgeSeconds = 86400)
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeHttp3ListenerReadiness(Configured: true, DefaultEnabled: true, EnablementLevel: enablementLevel, EnabledForTraffic: true, DisabledReason: disabledReason, AltSvcConfigured: true, AltSvcMaxAgeSeconds: altSvcMaxAgeSeconds, UdpQuicListenerIdentityModeled: true, QuicIdentity: null));
        }

        static void AssertHttp3ListenerReadinessProjectionRejects(string enablementLevel = "default", string disabledReason = "default_enabled", int altSvcMaxAgeSeconds = 86400)
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeHttp3ListenerReadinessProjection(Configured: true, DefaultEnabled: true, EnablementLevel: enablementLevel, EnabledForTraffic: true, DisabledReason: disabledReason, AltSvcConfigured: true, AltSvcMaxAgeSeconds: altSvcMaxAgeSeconds, UdpQuicListenerIdentityModeled: true, QuicIdentity: null));
        }

        static void AssertHttp2LimitsReject(int maxConcurrentStreams = 100, int maxHeaderListBytes = 32768, int maxFrameSize = 16384)
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeHttp2Limits(maxConcurrentStreams, maxHeaderListBytes, maxFrameSize));
        }

        static void AssertHttp2LimitsProjectionReject(int maxConcurrentStreams = 100, int maxHeaderListBytes = 32768, int maxFrameSize = 16384)
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeHttp2LimitsProjection(maxConcurrentStreams, maxHeaderListBytes, maxFrameSize));
        }

        static void AssertRuntimeListenerRejects(string name = "main", string address = "127.0.0.1", int port = 18080, RuntimeListenerTransport transport = RuntimeListenerTransport.Http, int backlog = 128, int maxRequestHeadBytes = 32768, int maxResponseHeadBytes = 32768, int maxChunkLineBytes = 8192, int forwardingBufferBytes = 8192, string? defaultCertificateId = null)
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeListener(Name: name, Address: address, Port: port, Enabled: true, Transport: transport, DefaultCertificateId: defaultCertificateId, SniCertificates: [], Backlog: backlog, MaxRequestHeadBytes: maxRequestHeadBytes, MaxResponseHeadBytes: maxResponseHeadBytes, MaxChunkLineBytes: maxChunkLineBytes, ForwardingBufferBytes: forwardingBufferBytes));
        }

        static void AssertRuntimeListenerIdentityRejects(string name = "main", string address = "127.0.0.1", int port = 18080, RuntimeListenerTransport transport = RuntimeListenerTransport.Http)
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeListenerIdentity(name, address, port, transport, TlsEnabled: false));
        }

        static void AssertRuntimeListenerIdentityProjectionRejects(string name = "main", string address = "127.0.0.1", int port = 18080, RuntimeListenerTransport transport = RuntimeListenerTransport.Http, string key = "main", string bindKey = "127.0.0.1|18080|http")
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeListenerIdentityProjection(name, address, port, transport, TlsEnabled: false, key, bindKey));
        }

        void AssertRuntimeListenerProjectionRejects(string name = "main", string address = "127.0.0.1", int port = 18080, RuntimeListenerTransport transport = RuntimeListenerTransport.Http, int backlog = 128, int maxRequestHeadBytes = 32768, int maxResponseHeadBytes = 32768, int maxChunkLineBytes = 8192, int forwardingBufferBytes = 8192, string? defaultCertificateId = "home-cert")
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeListenerProjection(Name: name, Address: address, Port: port, Enabled: listener.Enabled, Transport: transport, DefaultCertificateId: defaultCertificateId, SniCertificates: [], Backlog: backlog, MaxRequestHeadBytes: maxRequestHeadBytes, MaxResponseHeadBytes: maxResponseHeadBytes, MaxChunkLineBytes: maxChunkLineBytes, ForwardingBufferBytes: forwardingBufferBytes, Identity: listener.Identity, Protocols: listener.Protocols, Http3Enablement: listener.Http3Enablement, Http3AltSvc: listener.Http3AltSvc, Http2Limits: listener.Http2Limits, TcpTrafficEnabled: listener.TcpTrafficEnabled, Http3ProtocolConfigured: listener.Http3ProtocolConfigured, QuicIdentity: listener.QuicIdentity, Http3: listener.Http3));
        }

        static void AssertRuntimeQuicListenerIdentityRejects(string name = "main", string address = "127.0.0.1", int port = 18080)
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeQuicListenerIdentity(name, address, port, TlsEnabled: true));
        }

        static void AssertRuntimeQuicListenerIdentityProjectionRejects(string name = "main", string address = "127.0.0.1", int port = 18080, string key = "main|quic", string bindKey = "127.0.0.1|18080|udp|quic")
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeQuicListenerIdentityProjection(name, address, port, TlsEnabled: true, key, bindKey));
        }

        static void AssertRuntimeSniCertificateBindingRejects(string hostName = "owned.example.test", string certificateId = "owned-cert")
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeSniCertificateBinding(hostName, certificateId));
        }

        static void AssertRuntimeSniCertificateBindingProjectionRejects(string hostName = "owned.example.test", string certificateId = "owned-cert")
        {
            AssertEx.Throws<ArgumentException>(() => new RuntimeSniCertificateBindingProjection(hostName, certificateId));
        }
    }

    public static async Task ActiveInspectionProjectionUsesRouteReadModelsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var result = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        var projection = ProxyConfigurationReloadResultAssertions.Reloaded(result).ActiveConfiguration;
        var route = projection.Routes[0];
        var upstream = route.Upstreams[0];
        VerifyRouteInspectionIdentityAndViews(projection, route, upstream);
        VerifyHealthReadModelGuards();
        var failureStatusCodes = new List<int>
        {
            503
        };
        var directCircuitBreaker = new RuntimeCircuitBreakerProjection(Enabled: true, FailureThreshold: 2, SamplingWindow: TimeSpan.FromSeconds(30), OpenDuration: TimeSpan.FromSeconds(10), HalfOpenMaxAttempts: 1, FailureStatusCodes: failureStatusCodes);
        var directUpstream = new RuntimeUpstreamProjection(RouteName: "home", Name: "local-test", Scheme: "http", Protocol: "http1", Address: "127.0.0.1", Port: 15000, Weight: 1, Tls: new RuntimeUpstreamTlsProjection(false, null), Endpoint: "127.0.0.1:15000", UriEndpoint: "http://127.0.0.1:15000", EffectiveSniHost: "127.0.0.1", Identity: "home/local-test", CircuitBreaker: directCircuitBreaker);
        failureStatusCodes[0] = 502;
        failureStatusCodes.Clear();
        VerifyCircuitBreakerAndUpstreamReferenceGuards(directCircuitBreaker);
        VerifyUpstreamReadModelGuards();
        VerifyUpstreamCircuitBreakerCopies(directCircuitBreaker, directUpstream);
        var directRouteUpstreams = new List<RuntimeUpstreamProjection>
        {
            directUpstream
        };
        var directRoute = new RuntimeRouteProjection(Name: "home", Host: route.Host, PathPrefix: route.PathPrefix, Action: route.Action, LoadBalancingPolicy: route.LoadBalancingPolicy, HealthCheck: route.HealthCheck, Upstreams: directRouteUpstreams, HttpsRedirect: route.HttpsRedirect, CanonicalHost: route.CanonicalHost, HeaderPolicy: route.HeaderPolicy, PathRewrite: route.PathRewrite, Redirect: route.Redirect, StaticResponse: route.StaticResponse, Maintenance: route.Maintenance, Cache: route.Cache, ResolvedOptions: route.ResolvedOptions, SiteName: "home", Retry: route.Retry);
        directRouteUpstreams[0] = upstream;
        directRouteUpstreams.Clear();
        VerifyRouteReferenceGuards(route);
        VerifyRouteRuntimeGuards();
        VerifyRouteProjectionInputGuards();
        VerifyHttpsRedirectGuards();
        VerifyCanonicalHostGuards();
        VerifyRedirectGuards();
        VerifyPathRewriteGuards();
        VerifyStaticResponseGuards();
        VerifyMaintenanceGuards();
        VerifyTimeoutReadModelGuards();
        VerifyExplicitLongFlowTimeouts();
        VerifyObservabilityReadModelGuards();
        VerifyLogPersistenceReadModelGuards();
        VerifyRouteBudgetReadModelGuards();
        AssertEx.Equal("local-test", directRoute.Upstreams[0].Name);
        AssertEx.Equal("home", directRoute.SiteName);
        AssertEx.False(directRoute.Upstreams is RuntimeUpstreamProjection[]);
    }

    private static void VerifyUpstreamCircuitBreakerCopies(RuntimeCircuitBreakerProjection directCircuitBreaker, RuntimeUpstreamProjection directUpstream)
    {
        AssertEx.Equal(503, directCircuitBreaker.FailureStatusCodes[0]);
        AssertEx.Equal(503, directUpstream.CircuitBreaker.FailureStatusCodes[0]);
        AssertEx.Equal("home/local-test", directUpstream.Identity);
        AssertEx.False(directCircuitBreaker.FailureStatusCodes is int[]);
    }

    private static void VerifyRouteReferenceGuards(RuntimeRouteProjection route)
    {
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRouteProjection(Name: "home", Host: route.Host, PathPrefix: route.PathPrefix, Action: route.Action, LoadBalancingPolicy: route.LoadBalancingPolicy, HealthCheck: null!, Upstreams: [], HttpsRedirect: route.HttpsRedirect, CanonicalHost: route.CanonicalHost, HeaderPolicy: route.HeaderPolicy, PathRewrite: route.PathRewrite, Redirect: route.Redirect, StaticResponse: route.StaticResponse, Maintenance: route.Maintenance, Cache: route.Cache, ResolvedOptions: route.ResolvedOptions, SiteName: "home", Retry: route.Retry));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRouteProjection(Name: "home", Host: route.Host, PathPrefix: route.PathPrefix, Action: route.Action, LoadBalancingPolicy: route.LoadBalancingPolicy, HealthCheck: route.HealthCheck, Upstreams: null!, HttpsRedirect: route.HttpsRedirect, CanonicalHost: route.CanonicalHost, HeaderPolicy: route.HeaderPolicy, PathRewrite: route.PathRewrite, Redirect: route.Redirect, StaticResponse: route.StaticResponse, Maintenance: route.Maintenance, Cache: route.Cache, ResolvedOptions: route.ResolvedOptions, SiteName: "home", Retry: route.Retry));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeRouteProjection(Name: "home", Host: route.Host, PathPrefix: route.PathPrefix, Action: route.Action, LoadBalancingPolicy: route.LoadBalancingPolicy, HealthCheck: route.HealthCheck, Upstreams: [], HttpsRedirect: route.HttpsRedirect, CanonicalHost: route.CanonicalHost, HeaderPolicy: route.HeaderPolicy, PathRewrite: route.PathRewrite, Redirect: route.Redirect, StaticResponse: route.StaticResponse, Maintenance: route.Maintenance, Cache: route.Cache, ResolvedOptions: route.ResolvedOptions, SiteName: "home", Retry: null!));
    }

    private static void VerifyCircuitBreakerAndUpstreamReferenceGuards(RuntimeCircuitBreakerProjection directCircuitBreaker)
    {
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCircuitBreakerProjection(Enabled: true, FailureThreshold: 2, SamplingWindow: TimeSpan.FromSeconds(30), OpenDuration: TimeSpan.FromSeconds(10), HalfOpenMaxAttempts: 1, FailureStatusCodes: null!));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCircuitBreakerProjection(Enabled: true, FailureThreshold: 0, SamplingWindow: TimeSpan.FromSeconds(30), OpenDuration: TimeSpan.FromSeconds(10), HalfOpenMaxAttempts: 1, FailureStatusCodes: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCircuitBreakerProjection(Enabled: true, FailureThreshold: 2, SamplingWindow: TimeSpan.Zero, OpenDuration: TimeSpan.FromSeconds(10), HalfOpenMaxAttempts: 1, FailureStatusCodes: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCircuitBreakerProjection(Enabled: true, FailureThreshold: 2, SamplingWindow: TimeSpan.FromSeconds(30), OpenDuration: TimeSpan.Zero, HalfOpenMaxAttempts: 1, FailureStatusCodes: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new RuntimeCircuitBreakerProjection(Enabled: true, FailureThreshold: 2, SamplingWindow: TimeSpan.FromSeconds(30), OpenDuration: TimeSpan.FromSeconds(10), HalfOpenMaxAttempts: 0, FailureStatusCodes: []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeUpstreamProjection(RouteName: "home", Name: "local-test", Scheme: "http", Protocol: "http1", Address: "127.0.0.1", Port: 15000, Weight: 1, Tls: null!, Endpoint: "127.0.0.1:15000", UriEndpoint: "http://127.0.0.1:15000", EffectiveSniHost: "127.0.0.1", Identity: "home/local-test", CircuitBreaker: directCircuitBreaker));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeUpstreamProjection(RouteName: "home", Name: "local-test", Scheme: "http", Protocol: "http1", Address: "127.0.0.1", Port: 15000, Weight: 1, Tls: new RuntimeUpstreamTlsProjection(false, null), Endpoint: "127.0.0.1:15000", UriEndpoint: "http://127.0.0.1:15000", EffectiveSniHost: "127.0.0.1", Identity: "home/local-test", CircuitBreaker: null!));
    }

    private static void VerifyRouteProjectionInputGuards()
    {
        AssertRuntimeRouteProjectionRejects(name: null!);
        AssertRuntimeRouteProjectionRejects(name: " ");
        AssertRuntimeRouteProjectionRejects(host: null!);
        AssertRuntimeRouteProjectionRejects(host: " ");
        AssertRuntimeRouteProjectionRejects(pathPrefix: null!);
        AssertRuntimeRouteProjectionRejects(pathPrefix: " ");
        AssertRuntimeRouteProjectionRejects(pathPrefix: "api");
        AssertRuntimeRouteProjectionRejects(action: (RuntimeRouteAction)99);
        AssertRuntimeRouteProjectionRejects(loadBalancingPolicy: null!);
        AssertRuntimeRouteProjectionRejects(loadBalancingPolicy: " ");
        AssertRuntimeRouteProjectionRejects(siteName: null!);
    }

    private static void VerifyRouteInspectionIdentityAndViews(ProxyConfigurationProjection projection, RuntimeRouteProjection route, RuntimeUpstreamProjection upstream)
    {
        object routeCollection = projection.Routes;
        object upstreamCollection = route.Upstreams;
        AssertEx.Equal("home", route.Name);
        AssertEx.Equal("home", route.SiteName);
        AssertEx.Equal(1, route.Upstreams.Count);
        AssertEx.Equal("home", upstream.RouteName);
        AssertEx.Equal("local-test", upstream.Name);
        AssertEx.Equal("127.0.0.1:15000", upstream.Endpoint);
        AssertEx.False(routeCollection is RuntimeRoute[]);
        AssertEx.False(routeCollection is RuntimeRouteProjection[]);
        AssertEx.False(upstreamCollection is RuntimeUpstream[]);
        AssertEx.False(upstreamCollection is RuntimeUpstreamProjection[]);
        object healthCheck = route.HealthCheck;
        AssertEx.True(healthCheck is RuntimeHealthCheckProjection);
        AssertEx.False(healthCheck is RuntimeHealthCheckOptions);
    }

    private static void VerifyHealthReadModelGuards()
    {
        AssertHealthCheckOptionsRejects(path: null!);
        AssertHealthCheckOptionsRejects(path: "health");
        AssertHealthCheckOptionsRejects(interval: TimeSpan.Zero);
        AssertHealthCheckOptionsRejects(interval: TimeSpan.FromSeconds(3601));
        AssertHealthCheckOptionsRejects(timeout: TimeSpan.Zero);
        AssertHealthCheckOptionsRejects(timeout: TimeSpan.FromSeconds(301), interval: TimeSpan.FromSeconds(301));
        AssertHealthCheckOptionsRejects(interval: TimeSpan.FromSeconds(1), timeout: TimeSpan.FromSeconds(2));
        AssertHealthCheckOptionsRejects(healthyThreshold: 0);
        AssertHealthCheckOptionsRejects(healthyThreshold: 101);
        AssertHealthCheckOptionsRejects(unhealthyThreshold: 0);
        AssertHealthCheckOptionsRejects(unhealthyThreshold: 101);
        AssertHealthCheckProjectionRejects(path: null!);
        AssertHealthCheckProjectionRejects(path: "health");
        AssertHealthCheckProjectionRejects(interval: TimeSpan.Zero);
        AssertHealthCheckProjectionRejects(interval: TimeSpan.FromSeconds(3601));
        AssertHealthCheckProjectionRejects(timeout: TimeSpan.Zero);
        AssertHealthCheckProjectionRejects(timeout: TimeSpan.FromSeconds(301), interval: TimeSpan.FromSeconds(301));
        AssertHealthCheckProjectionRejects(interval: TimeSpan.FromSeconds(1), timeout: TimeSpan.FromSeconds(2));
        AssertHealthCheckProjectionRejects(healthyThreshold: 0);
        AssertHealthCheckProjectionRejects(healthyThreshold: 101);
        AssertHealthCheckProjectionRejects(unhealthyThreshold: 0);
        AssertHealthCheckProjectionRejects(unhealthyThreshold: 101);
    }

    private static void VerifyUpstreamReadModelGuards()
    {
        AssertRuntimeUpstreamRejects(routeName: null!);
        AssertRuntimeUpstreamRejects(routeName: " ");
        AssertRuntimeUpstreamRejects(name: null!);
        AssertRuntimeUpstreamRejects(name: " ");
        AssertRuntimeUpstreamRejects(scheme: null!);
        AssertRuntimeUpstreamRejects(scheme: " ");
        AssertRuntimeUpstreamRejects(scheme: "ftp");
        AssertRuntimeUpstreamRejects(protocol: null!);
        AssertRuntimeUpstreamRejects(protocol: " ");
        AssertRuntimeUpstreamRejects(protocol: "smtp");
        AssertRuntimeUpstreamRejects(scheme: "http", protocol: RuntimeUpstreamProtocol.Http2);
        AssertRuntimeUpstreamRejects(scheme: "http", protocol: RuntimeUpstreamProtocol.Http3);
        AssertRuntimeUpstreamRejects(address: null!);
        AssertRuntimeUpstreamRejects(address: " ");
        AssertRuntimeUpstreamRejects(port: 0);
        AssertRuntimeUpstreamRejects(port: 65536);
        AssertRuntimeUpstreamRejects(weight: 0);
        AssertRuntimeUpstreamRejects(weight: 100001);
        AssertRuntimeUpstreamTlsRejects(sniHost: " ");
        AssertRuntimeUpstreamTlsRejects(sniHost: " app.internal");
        AssertRuntimeUpstreamTlsRejects(sniHost: "https://app.internal");
        AssertRuntimeUpstreamTlsRejects(sniHost: "app.internal/path");
        AssertRuntimeUpstreamTlsRejects(sniHost: "app.internal:443");
        AssertRuntimeUpstreamTlsRejects(sniHost: "*.internal");
        VerifyUpstreamProjectionGuards();
    }

    private static void VerifyUpstreamProjectionGuards()
    {
        AssertRuntimeUpstreamProjectionRejects(routeName: null!);
        AssertRuntimeUpstreamProjectionRejects(routeName: " ");
        AssertRuntimeUpstreamProjectionRejects(name: null!);
        AssertRuntimeUpstreamProjectionRejects(name: " ");
        AssertRuntimeUpstreamProjectionRejects(scheme: null!);
        AssertRuntimeUpstreamProjectionRejects(scheme: " ");
        AssertRuntimeUpstreamProjectionRejects(scheme: "ftp");
        AssertRuntimeUpstreamProjectionRejects(protocol: null!);
        AssertRuntimeUpstreamProjectionRejects(protocol: " ");
        AssertRuntimeUpstreamProjectionRejects(protocol: "smtp");
        AssertRuntimeUpstreamProjectionRejects(scheme: "http", protocol: RuntimeUpstreamProtocol.Http2);
        AssertRuntimeUpstreamProjectionRejects(scheme: "http", protocol: RuntimeUpstreamProtocol.Http3);
        AssertRuntimeUpstreamProjectionRejects(address: null!);
        AssertRuntimeUpstreamProjectionRejects(address: " ");
        AssertRuntimeUpstreamProjectionRejects(port: 0);
        AssertRuntimeUpstreamProjectionRejects(port: 65536);
        AssertRuntimeUpstreamProjectionRejects(weight: 0);
        AssertRuntimeUpstreamProjectionRejects(weight: 100001);
        AssertRuntimeUpstreamProjectionRejects(endpoint: null!);
        AssertRuntimeUpstreamProjectionRejects(endpoint: " ");
        AssertRuntimeUpstreamProjectionRejects(uriEndpoint: null!);
        AssertRuntimeUpstreamProjectionRejects(uriEndpoint: " ");
        AssertRuntimeUpstreamProjectionRejects(effectiveSniHost: null!);
        AssertRuntimeUpstreamProjectionRejects(effectiveSniHost: " ");
        AssertRuntimeUpstreamProjectionRejects(identity: null!);
        AssertRuntimeUpstreamProjectionRejects(identity: " ");
        AssertRuntimeUpstreamTlsProjectionRejects(sniHost: " ");
        AssertRuntimeUpstreamTlsProjectionRejects(sniHost: " app.internal");
        AssertRuntimeUpstreamTlsProjectionRejects(sniHost: "https://app.internal");
        AssertRuntimeUpstreamTlsProjectionRejects(sniHost: "app.internal/path");
        AssertRuntimeUpstreamTlsProjectionRejects(sniHost: "app.internal:443");
        AssertRuntimeUpstreamTlsProjectionRejects(sniHost: "*.internal");
    }

    private static void VerifyRouteRuntimeGuards()
    {
        AssertRuntimeRouteRejects(name: null!);
        AssertRuntimeRouteRejects(name: " ");
        AssertRuntimeRouteRejects(host: null!);
        AssertRuntimeRouteRejects(host: " ");
        AssertRuntimeRouteRejects(pathPrefix: null!);
        AssertRuntimeRouteRejects(pathPrefix: " ");
        AssertRuntimeRouteRejects(pathPrefix: "api");
        AssertRuntimeRouteRejects(action: (RuntimeRouteAction)99);
        AssertRuntimeRouteRejects(loadBalancingPolicy: null!);
        AssertRuntimeRouteRejects(loadBalancingPolicy: " ");
        AssertRuntimeRouteRejects(siteName: null!);
    }

    private static void VerifyHttpsRedirectGuards()
    {
        AssertRuntimeHttpsRedirectRejects(statusCode: 300);
        AssertRuntimeHttpsRedirectRejects(statusCode: 309);
        AssertRuntimeHttpsRedirectRejects(httpsPort: 0);
        AssertRuntimeHttpsRedirectRejects(httpsPort: 65536);
        AssertRuntimeHttpsRedirectProjectionRejects(statusCode: 300);
        AssertRuntimeHttpsRedirectProjectionRejects(statusCode: 309);
        AssertRuntimeHttpsRedirectProjectionRejects(httpsPort: 0);
        AssertRuntimeHttpsRedirectProjectionRejects(httpsPort: 65536);
    }

    private static void VerifyCanonicalHostGuards()
    {
        AssertRuntimeCanonicalHostRejects(enabled: true, targetHost: "");
        AssertRuntimeCanonicalHostRejects(targetHost: null!);
        AssertRuntimeCanonicalHostRejects(targetHost: "api.test/path");
        AssertRuntimeCanonicalHostRejects(targetHost: "https://api.test");
        AssertRuntimeCanonicalHostRejects(targetHost: "api test");
        AssertRuntimeCanonicalHostRejects(statusCode: 300);
        AssertRuntimeCanonicalHostProjectionRejects(enabled: true, targetHost: "");
        AssertRuntimeCanonicalHostProjectionRejects(targetHost: null!);
        AssertRuntimeCanonicalHostProjectionRejects(targetHost: "api.test/path");
        AssertRuntimeCanonicalHostProjectionRejects(targetHost: "https://api.test");
        AssertRuntimeCanonicalHostProjectionRejects(targetHost: "api test");
        AssertRuntimeCanonicalHostProjectionRejects(statusCode: 300);
    }

    private static void VerifyRedirectGuards()
    {
        AssertRuntimeRedirectRejects(statusCode: 300);
        AssertRuntimeRedirectRejects(statusCode: 309);
        AssertRuntimeRedirectRejects(targetUrl: null!);
        AssertRuntimeRedirectRejects(targetPath: null!);
        AssertRuntimeRedirectRejects(targetPath: "redirect");
        AssertRuntimeRedirectProjectionRejects(statusCode: 300);
        AssertRuntimeRedirectProjectionRejects(statusCode: 309);
        AssertRuntimeRedirectProjectionRejects(targetUrl: null!);
        AssertRuntimeRedirectProjectionRejects(targetPath: null!);
        AssertRuntimeRedirectProjectionRejects(targetPath: "redirect");
    }

    private static void VerifyPathRewriteGuards()
    {
        AssertRuntimePathRewriteRejects(stripPrefix: null!);
        AssertRuntimePathRewriteRejects(replacePrefix: null!);
        AssertRuntimePathRewriteRejects(replacement: null!);
        AssertRuntimePathRewriteProjectionRejects(stripPrefix: null!);
        AssertRuntimePathRewriteProjectionRejects(replacePrefix: null!);
        AssertRuntimePathRewriteProjectionRejects(replacement: null!);
    }

    private static void VerifyStaticResponseGuards()
    {
        AssertRuntimeStaticResponseRejects(statusCode: 199);
        AssertRuntimeStaticResponseRejects(statusCode: 600);
        AssertRuntimeStaticResponseRejects(contentType: null!);
        AssertRuntimeStaticResponseRejects(contentType: " ");
        AssertRuntimeStaticResponseRejects(contentType: "text/plain\r\nX-Bad: value");
        AssertRuntimeStaticResponseRejects(body: null!);
        AssertRuntimeStaticResponseRejects(body: new string ('x', 64 * 1024 + 1));
        AssertRuntimeStaticResponseProjectionRejects(statusCode: 199);
        AssertRuntimeStaticResponseProjectionRejects(statusCode: 600);
        AssertRuntimeStaticResponseProjectionRejects(contentType: null!);
        AssertRuntimeStaticResponseProjectionRejects(contentType: " ");
        AssertRuntimeStaticResponseProjectionRejects(contentType: "text/plain\r\nX-Bad: value");
        AssertRuntimeStaticResponseProjectionRejects(body: null!);
        AssertRuntimeStaticResponseProjectionRejects(body: new string ('x', 64 * 1024 + 1));
    }

    private static void VerifyMaintenanceGuards()
    {
        AssertRuntimeMaintenanceRejects(retryAfterSeconds: -1);
        AssertRuntimeMaintenanceRejects(retryAfterSeconds: 86401);
        AssertRuntimeMaintenanceRejects(contentType: null!);
        AssertRuntimeMaintenanceRejects(contentType: " ");
        AssertRuntimeMaintenanceRejects(contentType: "text/plain\r\nX-Bad: value");
        AssertRuntimeMaintenanceRejects(body: null!);
        AssertRuntimeMaintenanceRejects(body: new string ('x', 64 * 1024 + 1));
        AssertRuntimeMaintenanceProjectionRejects(retryAfterSeconds: -1);
        AssertRuntimeMaintenanceProjectionRejects(retryAfterSeconds: 86401);
        AssertRuntimeMaintenanceProjectionRejects(contentType: null!);
        AssertRuntimeMaintenanceProjectionRejects(contentType: " ");
        AssertRuntimeMaintenanceProjectionRejects(contentType: "text/plain\r\nX-Bad: value");
        AssertRuntimeMaintenanceProjectionRejects(body: null!);
        AssertRuntimeMaintenanceProjectionRejects(body: new string ('x', 64 * 1024 + 1));
    }

    private static void VerifyTimeoutReadModelGuards()
    {
        AssertRuntimeTimeoutsRejects(clientRequestHeadTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsRejects(clientRequestBodyIdleTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsRejects(upstreamConnectTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsRejects(upstreamResponseHeadTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsRejects(upstreamResponseBodyIdleTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsRejects(downstreamWriteTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsRejects(tlsHandshakeTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsRejects(clientKeepAliveIdleTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsRejects(upstreamIdleConnectionLifetime: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsRejects(tunnelIdleTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeTimeoutsProjectionRejects(clientRequestHeadTimeout: TimeSpan.FromMilliseconds(600001));
        AssertRuntimeTimeoutsProjectionRejects(clientRequestBodyIdleTimeout: TimeSpan.FromMilliseconds(86400001));
        AssertRuntimeTimeoutsProjectionRejects(upstreamConnectTimeout: TimeSpan.FromMilliseconds(600001));
        AssertRuntimeTimeoutsProjectionRejects(upstreamResponseHeadTimeout: TimeSpan.FromMilliseconds(600001));
        AssertRuntimeTimeoutsProjectionRejects(upstreamResponseBodyIdleTimeout: TimeSpan.FromMilliseconds(86400001));
        AssertRuntimeTimeoutsProjectionRejects(downstreamWriteTimeout: TimeSpan.FromMilliseconds(86400001));
        AssertRuntimeTimeoutsProjectionRejects(tlsHandshakeTimeout: TimeSpan.FromMilliseconds(600001));
        AssertRuntimeTimeoutsProjectionRejects(clientKeepAliveIdleTimeout: TimeSpan.FromMilliseconds(600001));
        AssertRuntimeTimeoutsProjectionRejects(upstreamIdleConnectionLifetime: TimeSpan.FromMilliseconds(600001));
        AssertRuntimeTimeoutsProjectionRejects(tunnelIdleTimeout: TimeSpan.FromMilliseconds(600001));
    }

    private static void VerifyObservabilityReadModelGuards()
    {
        AssertRuntimeObservabilityRejects(recentDiagnosticsCapacity: 0);
        AssertRuntimeObservabilityRejects(recentDiagnosticsCapacity: 10001);
        AssertRuntimeObservabilityRejects(useNullLogPersistence: true);
        AssertRuntimeObservabilityProjectionRejects(recentDiagnosticsCapacity: 0);
        AssertRuntimeObservabilityProjectionRejects(recentDiagnosticsCapacity: 10001);
        AssertRuntimeObservabilityProjectionRejects(useNullLogPersistence: true);
    }

    private static void VerifyLogPersistenceReadModelGuards()
    {
        AssertRuntimeLogPersistenceRejects(maxFileBytes: 4095);
        AssertRuntimeLogPersistenceRejects(maxFileBytes: 1024L * 1024 * 1024 + 1);
        AssertRuntimeLogPersistenceRejects(maxFiles: 0);
        AssertRuntimeLogPersistenceRejects(maxFiles: 129);
        AssertRuntimeLogPersistenceProjectionRejects(maxFileBytes: 4095);
        AssertRuntimeLogPersistenceProjectionRejects(maxFileBytes: 1024L * 1024 * 1024 + 1);
        AssertRuntimeLogPersistenceProjectionRejects(maxFiles: 0);
        AssertRuntimeLogPersistenceProjectionRejects(maxFiles: 129);
    }

    private static void VerifyRouteBudgetReadModelGuards()
    {
        AssertRuntimeRouteResolvedOptionsRejects(maxRequestBodyBytes: -1);
        AssertRuntimeRouteResolvedOptionsRejects(maxRequestBodyBytes: 1L * 1024 * 1024 * 1024 * 1024 + 1);
        AssertRuntimeRouteResolvedOptionsRejects(clientRequestHeadTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeRouteResolvedOptionsRejects(clientRequestHeadTimeout: TimeSpan.FromMilliseconds(600001));
        AssertRuntimeRouteResolvedOptionsRejects(upstreamResponseHeadTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeRouteResolvedOptionsRejects(upstreamResponseHeadTimeout: TimeSpan.FromMilliseconds(600001));
        AssertRuntimeRouteResolvedOptionsProjectionRejects(maxRequestBodyBytes: -1);
        AssertRuntimeRouteResolvedOptionsProjectionRejects(maxRequestBodyBytes: 1L * 1024 * 1024 * 1024 * 1024 + 1);
        AssertRuntimeRouteResolvedOptionsProjectionRejects(clientRequestHeadTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeRouteResolvedOptionsProjectionRejects(clientRequestHeadTimeout: TimeSpan.FromMilliseconds(600001));
        AssertRuntimeRouteResolvedOptionsProjectionRejects(upstreamResponseHeadTimeout: TimeSpan.FromMilliseconds(99));
        AssertRuntimeRouteResolvedOptionsProjectionRejects(upstreamResponseHeadTimeout: TimeSpan.FromMilliseconds(600001));
    }

    private static void AssertHealthCheckOptionsRejects(string path = "/health", TimeSpan? interval = null, TimeSpan? timeout = null, int healthyThreshold = 1, int unhealthyThreshold = 1)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeHealthCheckOptions(Enabled: true, path, interval ?? TimeSpan.FromSeconds(2), timeout ?? TimeSpan.FromSeconds(1), healthyThreshold, unhealthyThreshold));
    }

    private static void AssertHealthCheckProjectionRejects(string path = "/health", TimeSpan? interval = null, TimeSpan? timeout = null, int healthyThreshold = 1, int unhealthyThreshold = 1)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeHealthCheckProjection(Enabled: true, path, interval ?? TimeSpan.FromSeconds(2), timeout ?? TimeSpan.FromSeconds(1), healthyThreshold, unhealthyThreshold));
    }

    private static void AssertRuntimeUpstreamRejects(string routeName = "home", string name = "local-test", string scheme = "http", string protocol = RuntimeUpstreamProtocol.Http1, string address = "127.0.0.1", int port = 15000, int weight = 1)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeUpstream(routeName, name, scheme, protocol, address, port, weight, RuntimeUpstreamTlsOptions.Default));
    }

    private static void AssertRuntimeUpstreamProjectionRejects(string routeName = "home", string name = "local-test", string scheme = "http", string protocol = RuntimeUpstreamProtocol.Http1, string address = "127.0.0.1", int port = 15000, int weight = 1, string endpoint = "127.0.0.1:15000", string uriEndpoint = "http://127.0.0.1:15000", string effectiveSniHost = "127.0.0.1", string identity = "home/local-test")
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeUpstreamProjection(routeName, name, scheme, protocol, address, port, weight, new RuntimeUpstreamTlsProjection(false, null), endpoint, uriEndpoint, effectiveSniHost, identity, new RuntimeCircuitBreakerProjection(Enabled: true, FailureThreshold: 2, SamplingWindow: TimeSpan.FromSeconds(30), OpenDuration: TimeSpan.FromSeconds(10), HalfOpenMaxAttempts: 1, FailureStatusCodes: [])));
    }

    private static void AssertRuntimeUpstreamTlsRejects(string? sniHost)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeUpstreamTlsOptions(ValidateCertificate: true, sniHost));
    }

    private static void AssertRuntimeUpstreamTlsProjectionRejects(string? sniHost)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeUpstreamTlsProjection(ValidateCertificate: true, sniHost));
    }

    private static void AssertRuntimeRouteRejects(string name = "home", string host = "home.test", string pathPrefix = "/", RuntimeRouteAction action = RuntimeRouteAction.Proxy, string loadBalancingPolicy = "round-robin", string siteName = "home")
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeRoute(name, host, pathPrefix, action, loadBalancingPolicy, new RuntimeHealthCheckOptions(Enabled: false, Path: "/health", Interval: TimeSpan.FromSeconds(2), Timeout: TimeSpan.FromSeconds(1), HealthyThreshold: 1, UnhealthyThreshold: 1), [], new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", "ok"), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), RuntimeCachePolicy.Disabled, new RuntimeRouteResolvedOptions(MaxRequestBodyBytes: 104857600, ClientRequestHeadTimeout: TimeSpan.FromSeconds(10), UpstreamResponseHeadTimeout: TimeSpan.FromSeconds(30), AccessLogEnabled: true), siteName, RuntimeRetryPolicy.Disabled));
    }

    private static void AssertRuntimeRouteProjectionRejects(string name = "home", string host = "home.test", string pathPrefix = "/", RuntimeRouteAction action = RuntimeRouteAction.Proxy, string loadBalancingPolicy = "round-robin", string siteName = "home")
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeRouteProjection(name, host, pathPrefix, action, loadBalancingPolicy, new RuntimeHealthCheckProjection(Enabled: false, Path: "/health", Interval: TimeSpan.FromSeconds(2), Timeout: TimeSpan.FromSeconds(1), HealthyThreshold: 1, UnhealthyThreshold: 1), [], new RuntimeHttpsRedirectProjection(false, 308, null), new RuntimeCanonicalHostProjection(false, "", 308), new RuntimeHeaderPolicyProjection([], [], [], []), new RuntimePathRewriteProjection("", "", ""), new RuntimeRedirectProjection(308, "", "", true), new RuntimeStaticResponseProjection(200, "text/plain; charset=utf-8", "ok"), new RuntimeMaintenanceProjection(false, null, "text/plain; charset=utf-8", "Service Unavailable"), new RuntimeCacheProjection(Enabled: false, MaxEntryBytes: 0, MaxTotalBytes: 0, DefaultTtl: TimeSpan.Zero, RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []), new RuntimeRouteResolvedOptionsProjection(MaxRequestBodyBytes: 104857600, ClientRequestHeadTimeout: TimeSpan.FromSeconds(10), UpstreamResponseHeadTimeout: TimeSpan.FromSeconds(30), AccessLogEnabled: true), siteName, new RuntimeRetryProjection(Enabled: false, MaxAttempts: 1, PerAttemptTimeout: null, RetryOnConnectFailure: false, RetryOnUpstreamResponseHeadTimeout: false, RetryOnStatusCodes: [], RetryMethods: [], RetryBackoff: TimeSpan.Zero)));
    }

    private static void AssertRuntimeHttpsRedirectRejects(int statusCode = 308, int? httpsPort = null)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeHttpsRedirectPolicy(Enabled: true, statusCode, httpsPort));
    }

    private static void AssertRuntimeHttpsRedirectProjectionRejects(int statusCode = 308, int? httpsPort = null)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeHttpsRedirectProjection(Enabled: true, statusCode, httpsPort));
    }

    private static void AssertRuntimeCanonicalHostRejects(bool enabled = false, string targetHost = "api.test", int statusCode = 308)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeCanonicalHostPolicy(enabled, targetHost, statusCode));
    }

    private static void AssertRuntimeCanonicalHostProjectionRejects(bool enabled = false, string targetHost = "api.test", int statusCode = 308)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeCanonicalHostProjection(enabled, targetHost, statusCode));
    }

    private static void AssertRuntimeRedirectRejects(int statusCode = 308, string targetUrl = "", string targetPath = "/redirect")
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeRedirectPolicy(statusCode, targetUrl, targetPath, PreserveQuery: true));
    }

    private static void AssertRuntimeRedirectProjectionRejects(int statusCode = 308, string targetUrl = "", string targetPath = "/redirect")
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeRedirectProjection(statusCode, targetUrl, targetPath, PreserveQuery: true));
    }

    private static void AssertRuntimePathRewriteRejects(string stripPrefix = "", string replacePrefix = "", string replacement = "")
    {
        AssertEx.Throws<ArgumentNullException>(() => new RuntimePathRewritePolicy(stripPrefix, replacePrefix, replacement));
    }

    private static void AssertRuntimePathRewriteProjectionRejects(string stripPrefix = "", string replacePrefix = "", string replacement = "")
    {
        AssertEx.Throws<ArgumentNullException>(() => new RuntimePathRewriteProjection(stripPrefix, replacePrefix, replacement));
    }

    private static void AssertRuntimeStaticResponseRejects(int statusCode = 200, string contentType = "text/plain; charset=utf-8", string body = "ok")
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeStaticResponse(statusCode, contentType, body));
    }

    private static void AssertRuntimeStaticResponseProjectionRejects(int statusCode = 200, string contentType = "text/plain; charset=utf-8", string body = "ok")
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeStaticResponseProjection(statusCode, contentType, body));
    }

    private static void AssertRuntimeMaintenanceRejects(int? retryAfterSeconds = null, string contentType = "text/plain; charset=utf-8", string body = "Service Unavailable")
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeMaintenancePolicy(Enabled: true, retryAfterSeconds, contentType, body));
    }

    private static void AssertRuntimeMaintenanceProjectionRejects(int? retryAfterSeconds = null, string contentType = "text/plain; charset=utf-8", string body = "Service Unavailable")
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeMaintenanceProjection(Enabled: true, retryAfterSeconds, contentType, body));
    }

    private static void AssertRuntimeRouteResolvedOptionsRejects(long maxRequestBodyBytes = 104857600, TimeSpan? clientRequestHeadTimeout = null, TimeSpan? upstreamResponseHeadTimeout = null)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeRouteResolvedOptions(maxRequestBodyBytes, clientRequestHeadTimeout ?? TimeSpan.FromSeconds(10), upstreamResponseHeadTimeout ?? TimeSpan.FromSeconds(30), AccessLogEnabled: true));
    }

    private static void AssertRuntimeRouteResolvedOptionsProjectionRejects(long maxRequestBodyBytes = 104857600, TimeSpan? clientRequestHeadTimeout = null, TimeSpan? upstreamResponseHeadTimeout = null)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeRouteResolvedOptionsProjection(maxRequestBodyBytes, clientRequestHeadTimeout ?? TimeSpan.FromSeconds(10), upstreamResponseHeadTimeout ?? TimeSpan.FromSeconds(30), AccessLogEnabled: true));
    }

    private static void AssertRuntimeTimeoutsRejects(TimeSpan? clientRequestHeadTimeout = null, TimeSpan? clientRequestBodyIdleTimeout = null, TimeSpan? upstreamConnectTimeout = null, TimeSpan? upstreamResponseHeadTimeout = null, TimeSpan? upstreamResponseBodyIdleTimeout = null, TimeSpan? downstreamWriteTimeout = null, TimeSpan? tlsHandshakeTimeout = null, TimeSpan? clientKeepAliveIdleTimeout = null, TimeSpan? upstreamIdleConnectionLifetime = null, TimeSpan? tunnelIdleTimeout = null)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeTimeouts(clientRequestHeadTimeout ?? TimeSpan.FromSeconds(10), clientRequestBodyIdleTimeout ?? TimeSpan.FromSeconds(10), upstreamConnectTimeout ?? TimeSpan.FromSeconds(10), upstreamResponseHeadTimeout ?? TimeSpan.FromSeconds(10), upstreamResponseBodyIdleTimeout ?? TimeSpan.FromSeconds(10), downstreamWriteTimeout ?? TimeSpan.FromSeconds(10), tlsHandshakeTimeout ?? TimeSpan.FromSeconds(10), clientKeepAliveIdleTimeout ?? TimeSpan.FromSeconds(10), upstreamIdleConnectionLifetime ?? TimeSpan.FromSeconds(10), tunnelIdleTimeout ?? TimeSpan.FromSeconds(10)));
    }

    private static void AssertRuntimeTimeoutsProjectionRejects(TimeSpan? clientRequestHeadTimeout = null, TimeSpan? clientRequestBodyIdleTimeout = null, TimeSpan? upstreamConnectTimeout = null, TimeSpan? upstreamResponseHeadTimeout = null, TimeSpan? upstreamResponseBodyIdleTimeout = null, TimeSpan? downstreamWriteTimeout = null, TimeSpan? tlsHandshakeTimeout = null, TimeSpan? clientKeepAliveIdleTimeout = null, TimeSpan? upstreamIdleConnectionLifetime = null, TimeSpan? tunnelIdleTimeout = null)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeTimeoutsProjection(clientRequestHeadTimeout ?? TimeSpan.FromSeconds(10), clientRequestBodyIdleTimeout ?? TimeSpan.FromSeconds(10), upstreamConnectTimeout ?? TimeSpan.FromSeconds(10), upstreamResponseHeadTimeout ?? TimeSpan.FromSeconds(10), upstreamResponseBodyIdleTimeout ?? TimeSpan.FromSeconds(10), downstreamWriteTimeout ?? TimeSpan.FromSeconds(10), tlsHandshakeTimeout ?? TimeSpan.FromSeconds(10), clientKeepAliveIdleTimeout ?? TimeSpan.FromSeconds(10), upstreamIdleConnectionLifetime ?? TimeSpan.FromSeconds(10), tunnelIdleTimeout ?? TimeSpan.FromSeconds(10)));
    }

    private static void AssertRuntimeObservabilityRejects(int recentDiagnosticsCapacity = 500, RuntimeLogPersistenceOptions? logPersistence = null, bool useNullLogPersistence = false)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeObservabilityOptions(AccessLogEnabled: true, recentDiagnosticsCapacity, useNullLogPersistence ? null! : logPersistence ?? CreateValidLogPersistenceOptions()));
        static RuntimeLogPersistenceOptions CreateValidLogPersistenceOptions()
        {
            return new RuntimeLogPersistenceOptions(AccessLogEnabled: true, AdminAuditEnabled: true, MaxFileBytes: 1_048_576, MaxFiles: 8);
        }
    }

    private static void AssertRuntimeObservabilityProjectionRejects(int recentDiagnosticsCapacity = 500, RuntimeLogPersistenceProjection? logPersistence = null, bool useNullLogPersistence = false)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeObservabilityProjection(AccessLogEnabled: true, recentDiagnosticsCapacity, useNullLogPersistence ? null! : logPersistence ?? CreateValidLogPersistenceProjection()));
        static RuntimeLogPersistenceProjection CreateValidLogPersistenceProjection()
        {
            return new RuntimeLogPersistenceProjection(AccessLogEnabled: true, AdminAuditEnabled: true, MaxFileBytes: 1_048_576, MaxFiles: 8);
        }
    }

    private static void AssertRuntimeLogPersistenceRejects(long maxFileBytes = 1_048_576, int maxFiles = 8)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeLogPersistenceOptions(AccessLogEnabled: true, AdminAuditEnabled: true, maxFileBytes, maxFiles));
    }

    private static void AssertRuntimeLogPersistenceProjectionRejects(long maxFileBytes = 1_048_576, int maxFiles = 8)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeLogPersistenceProjection(AccessLogEnabled: true, AdminAuditEnabled: true, maxFileBytes, maxFiles));
    }

    public static async Task ConfigReloadControllerReturnsConfigurationResponseAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var controller = new ProxyConfigurationController(new ProxyConfigurationAdministrationService(CreateNormalizer(), service), CreateReadAdministration(store), new ProxyConfigurationReloadAdministrationService<ProxyConfigurationProjection>(service));
        var actionResult = await controller.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        var ok = (OkObjectResult)AssertEx.NotNull(actionResult.Result);
        var response = (ProxyConfigurationReloadResponse)AssertEx.NotNull(ok.Value);
        var activeConfiguration = AssertEx.NotNull(response.ActiveConfiguration);
        AssertEx.True(response.Succeeded, string.Join("; ", response.Errors));
        AssertEx.Equal(1, activeConfiguration.Version);
        AssertEx.Equal("home", activeConfiguration.Routes[0].Name);
        AssertEx.Equal("home", activeConfiguration.Routes[0].Upstreams[0].RouteName);
        AssertEx.True(AssertEx.NotNull(response.ListenerReload).Succeeded);
    }

    public static void ConfigReadOperationsProjectActiveAndEffectiveFromCurrentSource()
    {
        var projection = new TestConfigurationProjection("current");
        var operations = new ProxyConfigurationReadOperations<TestConfigurationProjection>(new FixedConfigurationReadProjectionSource<TestConfigurationProjection>(projection));
        var missingOperations = new ProxyConfigurationReadOperations<TestConfigurationProjection>(new FixedConfigurationReadProjectionSource<TestConfigurationProjection>(null));
        var active = operations.ReadActive();
        var effective = operations.ReadEffective();
        var missingActive = missingOperations.ReadActive();
        var missingEffective = missingOperations.ReadEffective();
        AssertEx.True(active is ProxyConfigurationReadResult<TestConfigurationProjection>.AvailableResult);
        AssertEx.Equal(projection, ((ProxyConfigurationReadResult<TestConfigurationProjection>.AvailableResult)active).Configuration);
        AssertEx.True(effective is ProxyConfigurationReadResult<TestConfigurationProjection>.AvailableResult);
        AssertEx.Equal(projection, ((ProxyConfigurationReadResult<TestConfigurationProjection>.AvailableResult)effective).Configuration);
        AssertEx.True(missingActive is not ProxyConfigurationReadResult<TestConfigurationProjection>.AvailableResult);
        AssertEx.True(missingEffective is not ProxyConfigurationReadResult<TestConfigurationProjection>.AvailableResult);
    }

    public static async Task LoaderRejectsUnsafeHeaderRuleAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteCustomSite(temp.Path, "unsafe.json", """
            {
              "name": "unsafe",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": 18080
                }
              ],
              "host": "*",
              "routes": [
                {
                  "name": "app",
                  "pathPrefix": "/",
                  "action": "proxy",
                  "headerPolicy": {
                    "setRequestHeaders": [
                      {
                        "name": "Content-Length",
                        "value": "1"
                      }
                    ]
                  },
                  "upstreams": [
                    {
                      "name": "local",
                      "address": "127.0.0.1",
                      "port": 15000
                    }
                  ]
                }
              ]
            }
            """);
        var loader = CreateLoader(temp.Path);
        var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("restricted", StringComparison.OrdinalIgnoreCase)), string.Join("; ", result.Errors));
    }

    public static async Task ResponseHeaderPolicyCannotEmitHopByHopHeadersAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteCustomSite(temp.Path, "unsafe-response.json", """
            {
              "name": "unsafe-response",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": 18080
                }
              ],
              "host": "*",
              "routes": [
                {
                  "name": "app",
                  "pathPrefix": "/",
                  "action": "proxy",
                  "headerPolicy": {
                    "setResponseHeaders": [
                      {
                        "name": "Connection",
                        "value": "keep-alive"
                      }
                    ]
                  },
                  "upstreams": [
                    {
                      "name": "local",
                      "address": "127.0.0.1",
                      "port": 15000
                    }
                  ]
                }
              ]
            }
            """);
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("restricted", StringComparison.OrdinalIgnoreCase)), string.Join("; ", result.Errors));
    }

    public static async Task MultiFileConfigConflictReportingIsDeterministicAsync()
    {
        using var temp = TemporaryDirectory.Create();
        TestCertificates.WriteSelfSignedPfx(Path.Combine(temp.Path, "certs", "home.pfx"), "home.test");
        TestCertificates.WriteSelfSignedPfx(Path.Combine(temp.Path, "certs", "alt.pfx"), "alt.test");
        await File.WriteAllTextAsync(Path.Combine(Directory.CreateDirectory(Path.Combine(temp.Path, "config")).FullName, "proxy.json"), """
            {
              "certificates": [
                {
                  "id": "home-cert",
                  "format": "pfx",
                  "path": "certs/home.pfx"
                },
                {
                  "id": "alt-cert",
                  "format": "pfx",
                  "path": "certs/alt.pfx"
                }
              ]
            }
            """).ConfigureAwait(false);
        WriteHttpsSite(temp.Path, "home-a.json", port: 18443, upstreamPort: 15000, certificateId: "home-cert");
        WriteHttpsSite(temp.Path, "home-b.json", port: 18443, upstreamPort: 15001, certificateId: "alt-cert");
        var first = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var second = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(first);
        ProxyConfigurationLoadResultAssertions.AssertFailed(second);
        AssertEx.Equal(string.Join("\n", first.Errors), string.Join("\n", second.Errors));
        AssertEx.True(first.Errors.Any(static error => error.Contains("default certificate", StringComparison.OrdinalIgnoreCase)), string.Join("; ", first.Errors));
    }

    public static async Task ConfigValidateReportsValidWithoutApplyingAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "sites", "home.json"), SiteJson("home", 18081, 15001)).ConfigureAwait(false);
        var validation = await service.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        AssertEx.True(validation is ProxyConfigurationValidationResult.ValidResult, string.Join("; ", validation.Errors));
        AssertEx.Equal(1, store.Snapshot.Version);
        AssertEx.Equal(2, validation.WouldBeVersion);
        AssertEx.Equal(18080, store.Snapshot.Listeners[0].Port);
        var controller = new ProxyConfigurationController(new ProxyConfigurationAdministrationService(CreateNormalizer(), service), CreateReadAdministration(store), new ProxyConfigurationReloadAdministrationService<ProxyConfigurationProjection>(service));
        var actionResult = await controller.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        var ok = (OkObjectResult)AssertEx.NotNull(actionResult.Result);
        var response = (ProxyConfigurationValidationResponse)AssertEx.NotNull(ok.Value);
        AssertEx.True(response.Succeeded, string.Join("; ", response.Errors));
        AssertEx.Equal(2, response.WouldBeVersion);
    }

    public static async Task ConfigValidateReportsInvalidWithoutReplacingActiveConfigAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "sites", "broken.json"), "{ nope").ConfigureAwait(false);
        var validation = await service.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        AssertEx.True(validation is ProxyConfigurationValidationResult.InvalidResult);
        AssertEx.Equal(1, store.Snapshot.Version);
        AssertEx.True(validation.FileErrors.Any(error => error.Path?.EndsWith("broken.json", StringComparison.OrdinalIgnoreCase) == true));
        var controller = new ProxyConfigurationController(new ProxyConfigurationAdministrationService(CreateNormalizer(), service), CreateReadAdministration(store), new ProxyConfigurationReloadAdministrationService<ProxyConfigurationProjection>(service));
        var actionResult = await controller.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        var badRequest = (BadRequestObjectResult)AssertEx.NotNull(actionResult.Result);
        var response = (ProxyConfigurationValidationResponse)AssertEx.NotNull(badRequest.Value);
        AssertEx.False(response.Succeeded);
        AssertEx.True(response.FileErrors.Any(error => error.Path?.EndsWith("broken.json", StringComparison.OrdinalIgnoreCase) == true));
    }

    public static async Task ConfigNormalizeConvertsYamlToJsonWithoutApplyingAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        var normalizer = CreateNormalizer();
        var reloadAdministration = new ProxyConfigurationReloadAdministrationService<ProxyConfigurationProjection>(service);
        var controller = new ProxyConfigurationController(new ProxyConfigurationAdministrationService(normalizer, service), CreateReadAdministration(store), reloadAdministration);
        var actionResult = controller.Normalize(new ProxyConfigurationNormalizeSubmissionRequest("yaml", YamlSiteText("normalized", port: 18081, upstreamPort: 15001)));
        var ok = (OkObjectResult)AssertEx.NotNull(actionResult.Result);
        var normalize = (ProxyConfigurationNormalizeResponse)AssertEx.NotNull(ok.Value);
        AssertEx.True(normalize.Succeeded, string.Join("; ", normalize.Errors));
        AssertEx.True(AssertEx.NotNull(normalize.CanonicalJson).Contains("\"Name\": \"normalized\"", StringComparison.Ordinal));
        AssertEx.Equal(1, store.Snapshot.Version);
        AssertEx.Equal(18080, store.Snapshot.Listeners[0].Port);
    }

    public static void ConfigNormalizerShapesValidationFailuresFromParsedSite()
    {
        var parser = new FixedNormalizeSiteParser(ProxyConfigurationNormalizeSiteParseResult.Parsed(new SiteOptions { Name = "broken", Host = "*", PathPrefix = "/" }, "{}"));
        var normalizer = new ProxyConfigurationNormalizer(parser, new ProxyEndpointAddressPolicy(), new ProxyUrlSyntaxPolicy());
        var result = normalizer.Normalize(new ProxyConfigurationNormalizeRequest("yml", "ignored"));
        AssertEx.True(result is ProxyConfigurationNormalizeResult.FailedResult);
        AssertEx.Equal(ProxyConfigurationNormalizeFormat.Yaml, parser.LastFormat);
        AssertEx.Equal("yaml", result.Format);
        AssertEx.True(result.Errors.Any(static error => error.Contains("Proxy:Listeners", StringComparison.Ordinal)), string.Join("; ", result.Errors));
        AssertEx.True(result.FileErrors.All(static error => error.Path is null));
    }

    public static void ConfigNormalizerRejectsMissingRequestBody()
    {
        var normalizer = CreateNormalizer();
        var result = normalizer.Normalize(null);
        AssertEx.True(result is ProxyConfigurationNormalizeResult.FailedResult);
        AssertEx.Equal("unknown", result.Format);
        AssertEx.True(result.Errors.Any(static error => error.Contains("request body is required", StringComparison.Ordinal)), string.Join("; ", result.Errors));
        AssertEx.True(result.FileErrors.All(static error => error.Path is null));
    }

    public static void ConfigNormalizerRejectsIncompleteRequestFields()
    {
        var normalizer = CreateNormalizer();
        var missingFormat = normalizer.Normalize(new ProxyConfigurationNormalizeRequest(null, "ignored"));
        var missingText = normalizer.Normalize(new ProxyConfigurationNormalizeRequest("json", null));
        AssertEx.True(missingFormat is ProxyConfigurationNormalizeResult.FailedResult);
        AssertEx.Equal("unknown", missingFormat.Format);
        AssertEx.True(missingFormat.Errors.Any(static error => error.Contains("Format must be", StringComparison.Ordinal)), string.Join("; ", missingFormat.Errors));
        AssertEx.True(missingText is ProxyConfigurationNormalizeResult.FailedResult);
        AssertEx.Equal("json", missingText.Format);
        AssertEx.True(missingText.Errors.Any(static error => error.Contains("config text is required", StringComparison.Ordinal)), string.Join("; ", missingText.Errors));
        AssertEx.True(missingText.FileErrors.All(static error => error.Path is null));
    }

    public static void ConfigNormalizeControllerRejectsMissingRequestBody()
    {
        using var temp = TemporaryDirectory.Create();
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var reloadAdministration = new ProxyConfigurationReloadAdministrationService<ProxyConfigurationProjection>(service);
        var controller = new ProxyConfigurationController(new ProxyConfigurationAdministrationService(CreateNormalizer(), service), CreateReadAdministration(store), reloadAdministration);
        var actionResult = controller.Normalize(null);
        var badRequest = (BadRequestObjectResult)AssertEx.NotNull(actionResult.Result);
        var normalize = (ProxyConfigurationNormalizeResponse)AssertEx.NotNull(badRequest.Value);
        AssertEx.False(normalize.Succeeded);
        AssertEx.Equal("unknown", normalize.Format);
        AssertEx.True(normalize.Errors.Any(static error => error.Contains("request body is required", StringComparison.Ordinal)), string.Join("; ", normalize.Errors));
    }

    public static async Task EffectiveConfigResponseRedactsCertificateSecretsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var certificatePath = Path.Combine(temp.Path, "certs", "home.pfx");
        TestCertificates.WriteSelfSignedPfx(certificatePath, "home.test", "secret");
        WriteHttpsSite(temp.Path, "home.json", port: 18443, upstreamPort: 15000, certificateId: "home-cert");
        WriteOperationalConfig(temp.Path, certificateId: "home-cert", certificatePath: "certs/home.pfx", certificatePassword: "secret");
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var result = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(result, string.Join("; ", result.Errors));
        var reloadAdministration = new ProxyConfigurationReloadAdministrationService<ProxyConfigurationProjection>(service);
        var controller = new ProxyConfigurationController(new ProxyConfigurationAdministrationService(CreateNormalizer(), service), CreateReadAdministration(store), reloadAdministration);
        var actionResult = controller.Effective();
        var ok = (OkObjectResult)AssertEx.NotNull(actionResult.Result);
        var projection = (ProxyConfigurationResponse)AssertEx.NotNull(ok.Value);
        AssertEx.Equal(true, projection.Certificates[0].HasConfiguredPassword);
        AssertEx.False(projection.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task ExpiredCertificateProjectionKeepsValidityWindowVisibleAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var notBefore = DateTimeOffset.UtcNow.AddDays(-10);
        var notAfter = DateTimeOffset.UtcNow.AddDays(-1);
        var certificatePath = Path.Combine(temp.Path, "certs", "expired.pfx");
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
        await File.WriteAllBytesAsync(certificatePath, TestCertificates.CreateSelfSignedPfxBytesForValidity("expired.test", null, notBefore, notAfter)).ConfigureAwait(false);
        WriteHttpsSite(temp.Path, "expired.json", port: 18443, upstreamPort: 15000, certificateId: "expired-cert");
        WriteOperationalConfig(temp.Path, certificateId: "expired-cert", certificatePath: "certs/expired.pfx");
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot));
        AssertEx.True(projection.Certificates[0].NotAfter < DateTime.UtcNow);
        AssertEx.True(projection.Certificates[0].NotBefore < projection.Certificates[0].NotAfter);
    }

    public static async Task NotYetValidCertificateProjectionKeepsValidityWindowVisibleAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var notBefore = DateTimeOffset.UtcNow.AddDays(2);
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);
        var certificatePath = Path.Combine(temp.Path, "certs", "future.pfx");
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
        await File.WriteAllBytesAsync(certificatePath, TestCertificates.CreateSelfSignedPfxBytesForValidity("future.test", null, notBefore, notAfter)).ConfigureAwait(false);
        WriteHttpsSite(temp.Path, "future.json", port: 18443, upstreamPort: 15000, certificateId: "future-cert");
        WriteOperationalConfig(temp.Path, certificateId: "future-cert", certificatePath: "certs/future.pfx");
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot));
        AssertEx.True(projection.Certificates[0].NotBefore > DateTime.UtcNow);
        AssertEx.True(projection.Certificates[0].NotAfter > projection.Certificates[0].NotBefore);
    }

    public static async Task ReloadFailureReportsPerFileErrorAndPreservesActiveConfigAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        var loadedAt = store.Snapshot.LoadedAtUtc;
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "sites", "broken.json"), "{ nope").ConfigureAwait(false);
        var second = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Failed(second);
        AssertEx.Equal(1, store.Snapshot.Version);
        AssertEx.Equal(1, second.ActiveVersion);
        AssertEx.Equal(loadedAt, second.LastSuccessfulLoadAtUtc);
        AssertEx.True(second.FileErrors.Any(error => error.Path?.EndsWith("broken.json", StringComparison.OrdinalIgnoreCase) == true));
    }

    public static async Task ReloadWithInvalidLogPersistenceConfigPreservesActiveSnapshotAsync()
    {
        using var temp = TemporaryDirectory.Create();
        WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        WriteOperationalConfig(temp.Path, logMaxFileBytes: 8192, logMaxFiles: 2);
        var store = new ProxyConfigurationStore();
        var service = CreateReloadService(temp.Path, store);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        WriteOperationalConfig(temp.Path, logMaxFileBytes: 1024, logMaxFiles: 0);
        var second = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Failed(second);
        AssertEx.Equal(1, store.Snapshot.Version);
        AssertEx.Equal(8192L, store.Snapshot.Observability.LogPersistence.MaxFileBytes);
        AssertEx.Equal(2, store.Snapshot.Observability.LogPersistence.MaxFiles);
        AssertEx.True(second.Errors.Any(static error => error.Contains("MaxFileBytes", StringComparison.Ordinal)), string.Join("; ", second.Errors));
        AssertEx.True(second.Errors.Any(static error => error.Contains("MaxFiles", StringComparison.Ordinal)), string.Join("; ", second.Errors));
    }

    internal static void WriteSite(string dataDirectory, string fileName, int port, int upstreamPort)
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        File.WriteAllText(Path.Combine(sites, fileName), SiteJson(Path.GetFileNameWithoutExtension(fileName), port, upstreamPort));
    }

    internal static void WriteCustomSite(string dataDirectory, string fileName, string json)
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        File.WriteAllText(Path.Combine(sites, fileName), json);
    }

    internal static void WriteYamlSite(string dataDirectory, string fileName, int port, int upstreamPort)
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        File.WriteAllText(Path.Combine(sites, fileName), YamlSiteText(Path.GetFileNameWithoutExtension(fileName), port, upstreamPort));
    }

    private static string YamlSiteText(string name, int port, int upstreamPort)
    {
        return $$"""
        name: {{name}}
        listeners:
          - name: main
            address: 127.0.0.1
            port: {{port}}
        host: "*"
        pathPrefix: /
        upstreams:
          - name: local-test
            address: 127.0.0.1
            port: {{upstreamPort}}
        """;
    }

    internal static void WriteSiteWithTwoUpstreams(string dataDirectory, string fileName, int port, int firstUpstreamPort, int secondUpstreamPort, bool healthCheckEnabled = false, int healthIntervalSeconds = 1, int healthTimeoutSeconds = 1, int healthyThreshold = 1, int unhealthyThreshold = 1)
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        File.WriteAllText(Path.Combine(sites, fileName), $$"""
            {
              "name": "{{Path.GetFileNameWithoutExtension(fileName)}}",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{port}}
                }
              ],
              "host": "*",
              "pathPrefix": "/",
              "loadBalancingPolicy": "round-robin",
              "healthCheck": {
                "enabled": {{(healthCheckEnabled ? "true" : "false")}},
                "path": "/health",
                "intervalSeconds": {{healthIntervalSeconds}},
                "timeoutSeconds": {{healthTimeoutSeconds}},
                "healthyThreshold": {{healthyThreshold}},
                "unhealthyThreshold": {{unhealthyThreshold}}
              },
              "upstreams": [
                {
                  "name": "first",
                  "address": "127.0.0.1",
                  "port": {{firstUpstreamPort}},
                  "weight": 1
                },
                {
                  "name": "second",
                  "address": "127.0.0.1",
                  "port": {{secondUpstreamPort}},
                  "weight": 2
                }
              ]
            }
            """);
    }

    internal static void WriteHttpsSite(string dataDirectory, string fileName, int port, int upstreamPort, string certificateId, string host = "*", string sniHost = "home.test", bool includeDefault = true, bool duplicateSni = false)
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        var defaultCertificateLine = includeDefault ? $"""              "defaultCertificateId": "{certificateId}",""" : "";
        var duplicateSniBlock = duplicateSni ? $$"""
                    ,
                    {
                      "hostName": "{{sniHost}}",
                      "certificateId": "{{certificateId}}"
                    }
            """ : "";
        File.WriteAllText(Path.Combine(sites, fileName), $$"""
            {
              "name": "{{Path.GetFileNameWithoutExtension(fileName)}}",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{port}},
                  "transport": "https",
                  {{defaultCertificateLine}}
                  "sniCertificates": [
                    {
                      "hostName": "{{sniHost}}",
                      "certificateId": "{{certificateId}}"
                    }{{duplicateSniBlock}}
                  ]
                }
              ],
              "host": "{{host}}",
              "pathPrefix": "/",
              "upstreams": [
                {
                  "name": "local-test",
                  "address": "127.0.0.1",
                  "port": {{upstreamPort}}
                }
              ]
            }
            """);
    }

    internal static void WriteOperationalConfig(string dataDirectory, int clientRequestHeadTimeoutMs = 1000, int clientRequestBodyIdleTimeoutMs = 1000, int upstreamConnectTimeoutMs = 1000, int upstreamResponseHeadTimeoutMs = 1000, int upstreamResponseBodyIdleTimeoutMs = 1000, int downstreamWriteTimeoutMs = 1000, int tlsHandshakeTimeoutMs = 1000, int clientKeepAliveIdleTimeoutMs = 1000, int upstreamIdleConnectionLifetimeMs = 1000, int tunnelIdleTimeoutMs = 1000, int maxRequestsPerClientConnection = 100, int maxIdleUpstreamConnectionsPerUpstream = 16, int maxActiveUpgradedTunnels = 1024, bool accessLogEnabled = true, int recentDiagnosticsCapacity = 500, bool accessLogFileEnabled = true, bool adminAuditLogFileEnabled = true, long logMaxFileBytes = 1_048_576, int logMaxFiles = 8, int maxActiveClientConnections = 4096, int maxConcurrentTlsHandshakes = 128, int requestsPerMinutePerIp = 240, int upgradeRequestsPerMinutePerIp = 30, int maxRequestHeadBytes = 32768, int maxHeaderCount = 128, int maxHeaderLineBytes = 8192, long maxRequestBodyBytes = 104857600, int maxPathBytes = 8192, int shutdownGracePeriodSeconds = 15, bool forwardedHeadersEnabled = true, string[]? trustedProxies = null, string? certificateId = null, string? certificatePath = null, string? certificatePassword = null, string? certificatePasswordEnvironmentVariable = null)
    {
        var configDirectory = Directory.CreateDirectory(Path.Combine(dataDirectory, "config")).FullName;
        var certificatesJson = certificateId is null ? "[]" : $$"""
            [
                {
                  "id": "{{certificateId}}",
                  "format": "pfx",
                  "path": "{{certificatePath}}"
                  {{(certificatePassword is null ? "" : $""","password": "{certificatePassword}" """)}}
                  {{(certificatePasswordEnvironmentVariable is null ? "" : $""","passwordEnvironmentVariable": "{certificatePasswordEnvironmentVariable}" """)}}
                }
              ]
            """;
        var trustedProxiesJson = trustedProxies is null ? "[]" : "[" + string.Join(", ", trustedProxies.Select(static proxy => $@" ""{proxy}""")) + "]";
        File.WriteAllText(Path.Combine(configDirectory, "proxy.json"), $$"""
            {
              "timeouts": {
                "clientRequestHeadTimeoutMs": {{clientRequestHeadTimeoutMs}},
                "clientRequestBodyIdleTimeoutMs": {{clientRequestBodyIdleTimeoutMs}},
                "upstreamConnectTimeoutMs": {{upstreamConnectTimeoutMs}},
                "upstreamResponseHeadTimeoutMs": {{upstreamResponseHeadTimeoutMs}},
                "upstreamResponseBodyIdleTimeoutMs": {{upstreamResponseBodyIdleTimeoutMs}},
                "downstreamWriteTimeoutMs": {{downstreamWriteTimeoutMs}},
                "tlsHandshakeTimeoutMs": {{tlsHandshakeTimeoutMs}},
                "clientKeepAliveIdleTimeoutMs": {{clientKeepAliveIdleTimeoutMs}},
                "upstreamIdleConnectionLifetimeMs": {{upstreamIdleConnectionLifetimeMs}},
                "tunnelIdleTimeoutMs": {{tunnelIdleTimeoutMs}}
              },
              "connections": {
                "maxRequestsPerClientConnection": {{maxRequestsPerClientConnection}},
                "maxIdleUpstreamConnectionsPerUpstream": {{maxIdleUpstreamConnectionsPerUpstream}},
                "maxActiveUpgradedTunnels": {{maxActiveUpgradedTunnels}}
              },
              "observability": {
                "accessLogEnabled": {{(accessLogEnabled ? "true" : "false")}},
                "recentDiagnosticsCapacity": {{recentDiagnosticsCapacity}},
                "logPersistence": {
                  "accessLogEnabled": {{(accessLogFileEnabled ? "true" : "false")}},
                  "adminAuditEnabled": {{(adminAuditLogFileEnabled ? "true" : "false")}},
                  "maxFileBytes": {{logMaxFileBytes}},
                  "maxFiles": {{logMaxFiles}}
                }
              },
              "limits": {
                "maxActiveClientConnections": {{maxActiveClientConnections}},
                "maxConcurrentTlsHandshakes": {{maxConcurrentTlsHandshakes}},
                "requestsPerMinutePerIp": {{requestsPerMinutePerIp}},
                "upgradeRequestsPerMinutePerIp": {{upgradeRequestsPerMinutePerIp}},
                "maxRequestHeadBytes": {{maxRequestHeadBytes}},
                "maxHeaderCount": {{maxHeaderCount}},
                "maxHeaderLineBytes": {{maxHeaderLineBytes}},
                "maxRequestBodyBytes": {{maxRequestBodyBytes}},
                "maxPathBytes": {{maxPathBytes}},
                "shutdownGracePeriodSeconds": {{shutdownGracePeriodSeconds}}
              },
              "forwardedHeaders": {
                "enabled": {{(forwardedHeadersEnabled ? "true" : "false")}},
                "trustedProxies": {{trustedProxiesJson}}
              },
              "certificates": {{certificatesJson}}
            }
            """);
    }

    private static ProxyConfigurationLoader CreateLoader(string dataDirectory, TimeProvider? timeProvider = null)
    {
        return new ProxyConfigurationLoader(new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory }), new ProxyDataDirectoryBootstrapper(new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory })), new SiteConfigurationParser(), new ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, timeProvider ?? TimeProvider.System);
    }

    private static ProxyConfigurationReloadService CreateReloadService(string dataDirectory, ProxyConfigurationStore store)
    {
        return new ProxyConfigurationReloadService(CreateLoader(dataDirectory), store, store, new ResponseCacheStore(TimeProvider.System), new ProxyMetrics(), ActivatingProxyListenerReloadApplier.Instance, SilentProxyConfigurationReloadEventSink.Instance, TestHttp3PlatformSupport.ProjectionSource);
    }

    private static ProxyConfigurationReadAdministrationService<ProxyConfigurationProjection> CreateReadAdministration(IProxyConfigurationStore store)
    {
        return new ProxyConfigurationReadAdministrationService<ProxyConfigurationProjection>(new ProxyConfigurationReadOperations<ProxyConfigurationProjection>(new ProxyConfigurationReadProjectionSource(store, TestHttp3PlatformSupport.ProjectionSource)));
    }

    private static ProxyConfigurationNormalizer CreateNormalizer()
    {
        return new ProxyConfigurationNormalizer(new ProxyConfigurationNormalizeSiteParser(new SiteConfigurationParser()), new ProxyEndpointAddressPolicy(), new ProxyUrlSyntaxPolicy());
    }

    private sealed record TestConfigurationProjection(string Name);
    private sealed class FixedNormalizeSiteParser : IProxyConfigurationNormalizeSiteParser
    {
        private readonly ProxyConfigurationNormalizeSiteParseResult _result;
        public FixedNormalizeSiteParser(ProxyConfigurationNormalizeSiteParseResult result)
        {
            _result = result;
        }

        public ProxyConfigurationNormalizeFormat? LastFormat { get; private set; }

        public ProxyConfigurationNormalizeSiteParseResult Parse(string text, ProxyConfigurationNormalizeFormat format)
        {
            _ = text;
            LastFormat = format;
            return _result;
        }
    }

    private sealed class FixedConfigurationReadProjectionSource<TConfiguration> : IProxyConfigurationReadProjectionSource<TConfiguration> where TConfiguration : class
    {
        private readonly ProxyConfigurationReadProjectionResult<TConfiguration> _result;
        public FixedConfigurationReadProjectionSource(TConfiguration? projection)
        {
            _result = projection is null ? ProxyConfigurationReadProjectionResult<TConfiguration>.MissingConfiguration : ProxyConfigurationReadProjectionResult<TConfiguration>.Available(projection);
        }

        public ProxyConfigurationReadProjectionResult<TConfiguration> ReadCurrent()
        {
            return _result;
        }
    }

    private static string SiteJson(string name, int port, int upstreamPort)
    {
        return $$"""
        {
          "name": "{{name}}",
          "listeners": [
            {
              "name": "main",
              "address": "127.0.0.1",
              "port": {{port}}
            }
          ],
          "host": "*",
          "pathPrefix": "/",
          "upstreams": [
            {
              "name": "local-test",
              "address": "127.0.0.1",
              "port": {{upstreamPort}}
            }
          ]
        }
        """;
    }

    private static void AssertRuntimeMetricsOptionsRejects(string endpointPath = RuntimeMetricsOptions.FixedAdminEndpointPath)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeMetricsOptions(Enabled: true, EndpointPath: endpointPath, ProtectedByAdminAuth: true, IncludePerRouteLabels: true, IncludePerUpstreamLabels: false, PublicMetricsEnabled: false));
    }

    private static void AssertRuntimeMetricsProjectionRejects(string endpointPath = RuntimeMetricsProjection.FixedAdminEndpointPath)
    {
        AssertEx.Throws<ArgumentException>(() => new RuntimeMetricsProjection(Enabled: true, EndpointPath: endpointPath, ProtectedByAdminAuth: true, IncludePerRouteLabels: true, IncludePerUpstreamLabels: false, PublicMetricsEnabled: false));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
