using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class AcmeTests
{
    public static async Task ManualPfxCertificateBehaviorRemainsValid()
    {
        using var temp = TemporaryDirectory.Create();
        var certificatePath = Path.Combine(temp.Path, "certs", "manual.pfx");
        TestCertificates.WriteSelfSignedPfx(certificatePath, "manual.test", "secret");
        ConfigurationTests.WriteHttpsSite(temp.Path, "manual.json", 18443, 15000, "manual-cert");
        ConfigurationTests.WriteOperationalConfig(temp.Path, certificateId: "manual-cert", certificatePath: "certs/manual.pfx", certificatePassword: "secret");
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        var certificate = snapshot.Certificates["manual-cert"];
        AssertEx.Equal("manualPfx", certificate.Source);
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot));
        AssertEx.Equal("manualPfx", projection.Certificates[0].Source);
    }

    public static void AcmeConfigValidationRejectsMissingTermsAcceptance()
    {
        var failures = ProxyOperationalOptionsValidationRules.Validate(new ProxyOperationalOptions { Acme = new ProxyAcmeOptions { Enabled = true, TermsAccepted = false, Certificates = [new AcmeManagedCertificateOptions { Id = "home-acme", Domains = ["home.example.test"] }] } }, static _ => null, new Mk8.Drava.Application.INF.Configuration.ProxyAdminUrlPolicy(), new ProxyRelativeStoragePathPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy());
        AssertEx.True(failures.Any(static failure => failure.Contains("TermsAccepted", StringComparison.Ordinal)));
    }

    public static void Http01ChallengeReturnsExactTokenResponse()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new AcmeChallengeStore();
        AssertEx.True(store.Register("abc_123-token", "abc_123-token.thumbprint", time.GetUtcNow().AddMinutes(5))is not AcmeChallengeRegistrationResult.RejectedResult);
        var responder = new AcmeHttp01ChallengeResponder(store, time);
        var result = responder.CreateResponse(Request("GET", "/.well-known/acme-challenge/abc_123-token"));
        AssertEx.True(result is AcmeHttp01ChallengeResponseResult.HandledResult);
        var response = ((AcmeHttp01ChallengeResponseResult.HandledResult)result).Response;
        AssertEx.Equal(200, response.StatusCode);
        AssertEx.Equal("abc_123-token.thumbprint", response.Body);
    }

    public static void AcmeChallengeStoreNamesRegistrationAndLookupOutcomes()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new AcmeChallengeStore();
        var registered = store.Register("token", "token.thumbprint", time.GetUtcNow().AddMinutes(5));
        var invalidToken = store.Register("bad/token", "token.thumbprint", time.GetUtcNow().AddMinutes(5));
        var invalidBody = store.Register("token2", "", time.GetUtcNow().AddMinutes(5));
        var found = store.FindResponse("token", time.GetUtcNow());
        var missing = store.FindResponse("missing", time.GetUtcNow());
        var expiredToken = "expired";
        store.Register(expiredToken, "expired.thumbprint", time.GetUtcNow().AddSeconds(-1));
        var expired = store.FindResponse(expiredToken, time.GetUtcNow());
        AssertEx.True(registered is not AcmeChallengeRegistrationResult.RejectedResult);
        AssertEx.True(invalidToken is AcmeChallengeRegistrationResult.RejectedResult);
        AssertEx.Equal("invalid-token", ((AcmeChallengeRegistrationResult.RejectedResult)invalidToken).Reason);
        AssertEx.True(invalidBody is AcmeChallengeRegistrationResult.RejectedResult);
        AssertEx.Equal("invalid-response-body", ((AcmeChallengeRegistrationResult.RejectedResult)invalidBody).Reason);
        AssertEx.True(found is AcmeChallengeResponseLookupResult.FoundResult);
        AssertEx.Equal("token.thumbprint", ((AcmeChallengeResponseLookupResult.FoundResult)found).ResponseBody);
        AssertEx.True(missing is not AcmeChallengeResponseLookupResult.FoundResult);
        AssertEx.True(expired is not AcmeChallengeResponseLookupResult.FoundResult);
    }

    public static void UnknownHttp01ChallengeReturnsSafe404()
    {
        var responder = new AcmeHttp01ChallengeResponder(new AcmeChallengeStore(), new ManualTimeProvider(DateTimeOffset.UtcNow));
        var result = responder.CreateResponse(Request("GET", "/.well-known/acme-challenge/missing"));
        AssertEx.True(result is AcmeHttp01ChallengeResponseResult.HandledResult);
        var response = ((AcmeHttp01ChallengeResponseResult.HandledResult)result).Response;
        AssertEx.Equal(404, response.StatusCode);
    }

    public static void NonAcmeRequestDoesNotCreateHttp01ChallengeResponse()
    {
        var responder = new AcmeHttp01ChallengeResponder(new AcmeChallengeStore(), new ManualTimeProvider(DateTimeOffset.UtcNow));
        var result = responder.CreateResponse(Request("GET", "/regular"));
        AssertEx.True(result is AcmeHttp01ChallengeResponseResult.NoMatchResult);
    }

    public static void AcmeCertificateIssueResultNamesSuccessAndFailure()
    {
        var pfxBytes = new byte[]
        {
            1,
            2,
            3
        };
        var issued = AcmeCertificateIssueResult.Issued(pfxBytes);
        var failed = AcmeCertificateIssueResult.Failed("issuer failed");
        AssertEx.True(issued is AcmeCertificateIssueResult.IssuedResult);
        var issuedResult = (AcmeCertificateIssueResult.IssuedResult)issued;
        AssertEx.False(ReferenceEquals(pfxBytes, issuedResult.PfxBytes));
        pfxBytes[0] = 99;
        AssertEx.Equal(1, issuedResult.PfxBytes[0]);
        var returnedPfxBytes = issuedResult.PfxBytes;
        returnedPfxBytes[0] = 88;
        AssertEx.Equal(1, issuedResult.PfxBytes[0]);
        AssertEx.True(failed is AcmeCertificateIssueResult.FailedResult);
        AssertEx.Equal("issuer failed", ((AcmeCertificateIssueResult.FailedResult)failed).ErrorSummary);
    }

    public static void AcmeRequestAndStatusRecordsCopyInputCollections()
    {
        var issueDomains = new List<string>
        {
            "home.example.test"
        };
        var contactEmails = new List<string>
        {
            "ops@example.test"
        };
        var issue = new AcmeCertificateIssueRequest(CertificateId: "home-acme", Domains: issueDomains, DirectoryUrl: "https://acme.example.test/directory", ContactEmails: contactEmails, TermsAccepted: true);
        var writeDomains = new List<string>
        {
            "home.example.test"
        };
        var pfxBytes = new byte[]
        {
            1,
            2,
            3
        };
        var write = new AcmeCertificateMaterialWriteRequest(StoragePath: "acme", CertificateId: "home-acme", Domains: writeDomains, DataDirectory: "data", WrittenAtUtc: DateTimeOffset.UnixEpoch, PfxBytes: pfxBytes);
        var lifecycleDomains = new List<string>
        {
            "home.example.test"
        };
        var lifecycle = new AcmeCertificateLifecycleStatus(CertificateId: "home-acme", Enabled: true, Domains: lifecycleDomains, Active: true, Source: "acme", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: null, LastSucceededAtUtc: DateTimeOffset.UnixEpoch, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null);
        var certificates = new List<AcmeCertificateLifecycleStatus>
        {
            lifecycle
        };
        var status = new AcmeStatus(Enabled: true, DirectoryUrl: "https://acme.example.test/directory", UseStaging: false, Certificates: certificates);
        var replacementLifecycle = new AcmeCertificateLifecycleStatus(CertificateId: "replacement", Enabled: lifecycle.Enabled, Domains: lifecycle.Domains, Active: lifecycle.Active, Source: lifecycle.Source, NotBeforeUtc: lifecycle.NotBeforeUtc, NotAfterUtc: lifecycle.NotAfterUtc, RenewalDueAtUtc: lifecycle.RenewalDueAtUtc, LastAttemptAtUtc: lifecycle.LastAttemptAtUtc, LastSucceededAtUtc: lifecycle.LastSucceededAtUtc, LastFailedAtUtc: lifecycle.LastFailedAtUtc, NextAttemptNotBeforeUtc: lifecycle.NextAttemptNotBeforeUtc, LastResult: lifecycle.LastResult, ErrorSummary: lifecycle.ErrorSummary);
        issueDomains[0] = "replacement.example.test";
        contactEmails[0] = "security@example.test";
        writeDomains[0] = "write-replacement.example.test";
        pfxBytes[0] = 99;
        lifecycleDomains[0] = "lifecycle-replacement.example.test";
        certificates[0] = replacementLifecycle;
        issueDomains.Clear();
        contactEmails.Clear();
        writeDomains.Clear();
        lifecycleDomains.Clear();
        certificates.Clear();
        AssertEx.Equal("home.example.test", issue.Domains[0]);
        AssertEx.Equal("ops@example.test", issue.ContactEmails[0]);
        AssertEx.Equal("home.example.test", write.Domains[0]);
        AssertEx.Equal(1, write.PfxBytes[0]);
        var returnedWritePfxBytes = write.PfxBytes;
        returnedWritePfxBytes[0] = 88;
        AssertEx.Equal(1, write.PfxBytes[0]);
        AssertEx.Equal("home-acme", lifecycle.CertificateId);
        AssertEx.True(lifecycle.Enabled);
        AssertEx.True(lifecycle.Active);
        AssertEx.Equal("acme", lifecycle.Source);
        AssertEx.Equal("loaded", lifecycle.LastResult);
        AssertEx.Equal("home.example.test", lifecycle.Domains[0]);
        AssertEx.Equal("home-acme", status.Certificates[0].CertificateId);
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateIssueRequest(CertificateId: " ", Domains: ["home.example.test"], DirectoryUrl: "https://acme.example.test/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateIssueRequest(CertificateId: "home-acme", Domains: [], DirectoryUrl: "https://acme.example.test/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateIssueRequest(CertificateId: "home-acme", Domains: [" "], DirectoryUrl: "https://acme.example.test/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateIssueRequest(CertificateId: "home-acme", Domains: ["home.example.test"], DirectoryUrl: " ", ContactEmails: ["ops@example.test"], TermsAccepted: true));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateIssueRequest(CertificateId: "home-acme", Domains: ["home.example.test"], DirectoryUrl: "https://acme.example.test/directory", ContactEmails: [" "], TermsAccepted: true));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeCertificateIssueRequest(CertificateId: "home-acme", Domains: [null!], DirectoryUrl: "https://acme.example.test/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeCertificateIssueRequest(CertificateId: "home-acme", Domains: ["home.example.test"], DirectoryUrl: "https://acme.example.test/directory", ContactEmails: [null!], TermsAccepted: true));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateMaterialWriteRequest(StoragePath: " ", CertificateId: "home-acme", Domains: ["home.example.test"], DataDirectory: "data", WrittenAtUtc: DateTimeOffset.UnixEpoch, PfxBytes: [1]));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateMaterialWriteRequest(StoragePath: "acme", CertificateId: " ", Domains: ["home.example.test"], DataDirectory: "data", WrittenAtUtc: DateTimeOffset.UnixEpoch, PfxBytes: [1]));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateMaterialWriteRequest(StoragePath: "acme", CertificateId: "home-acme", Domains: [], DataDirectory: "data", WrittenAtUtc: DateTimeOffset.UnixEpoch, PfxBytes: [1]));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateMaterialWriteRequest(StoragePath: "acme", CertificateId: "home-acme", Domains: [" "], DataDirectory: "data", WrittenAtUtc: DateTimeOffset.UnixEpoch, PfxBytes: [1]));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateMaterialWriteRequest(StoragePath: "acme", CertificateId: "home-acme", Domains: ["home.example.test"], DataDirectory: " ", WrittenAtUtc: DateTimeOffset.UnixEpoch, PfxBytes: [1]));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateMaterialWriteRequest(StoragePath: "acme", CertificateId: "home-acme", Domains: ["home.example.test"], DataDirectory: "data", WrittenAtUtc: DateTimeOffset.UnixEpoch, PfxBytes: []));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeCertificateMaterialWriteRequest(StoragePath: "acme", CertificateId: "home-acme", Domains: [null!], DataDirectory: "data", WrittenAtUtc: DateTimeOffset.UnixEpoch, PfxBytes: [1]));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateLifecycleStatus(CertificateId: " ", Enabled: true, Domains: ["home.example.test"], Active: true, Source: "acme", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: null, LastSucceededAtUtc: DateTimeOffset.UnixEpoch, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateLifecycleStatus(CertificateId: "home-acme", Enabled: true, Domains: [], Active: true, Source: "acme", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: null, LastSucceededAtUtc: DateTimeOffset.UnixEpoch, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateLifecycleStatus(CertificateId: "home-acme", Enabled: true, Domains: [" "], Active: true, Source: "acme", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: null, LastSucceededAtUtc: DateTimeOffset.UnixEpoch, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateLifecycleStatus(CertificateId: "home-acme", Enabled: true, Domains: ["home.example.test"], Active: true, Source: " ", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: null, LastSucceededAtUtc: DateTimeOffset.UnixEpoch, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null));
        AssertEx.Throws<ArgumentException>(() => new AcmeCertificateLifecycleStatus(CertificateId: "home-acme", Enabled: true, Domains: ["home.example.test"], Active: true, Source: "acme", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: null, LastSucceededAtUtc: DateTimeOffset.UnixEpoch, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: " ", ErrorSummary: null));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeCertificateLifecycleStatus(CertificateId: "home-acme", Enabled: true, Domains: [null!], Active: true, Source: "acme", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: null, LastSucceededAtUtc: DateTimeOffset.UnixEpoch, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null));
        AssertEx.Throws<ArgumentNullException>(() => AcmeStatus.FromSources(enabled: true, directoryUrl: "https://acme.example.test/directory", useStaging: false, certificates: [null!]));
        var apiStatus = AcmeStatusResponseMapper.FromStatus(status);
        AssertEx.Equal("home-acme", apiStatus.Certificates[0].CertificateId);
        AssertEx.Equal("home.example.test", apiStatus.Certificates[0].Domains[0]);
        var responseDomains = new List<string>
        {
            "home.example.test"
        };
        var responseLifecycle = new AcmeCertificateLifecycleStatusResponse(certificateId: "home-acme", enabled: true, domains: responseDomains, active: true, source: "acme", notBeforeUtc: DateTimeOffset.UnixEpoch, notAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), renewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), lastAttemptAtUtc: null, lastSucceededAtUtc: DateTimeOffset.UnixEpoch, lastFailedAtUtc: null, nextAttemptNotBeforeUtc: null, lastResult: "loaded", errorSummary: null);
        var responseCertificates = new List<AcmeCertificateLifecycleStatusResponse>
        {
            responseLifecycle
        };
        var directStatus = new AcmeStatusResponse(enabled: true, directoryUrl: "https://acme.example.test/directory", useStaging: false, certificates: responseCertificates);
        responseDomains[0] = "response-replacement.example.test";
        responseCertificates[0] = new AcmeCertificateLifecycleStatusResponse(certificateId: "replacement", enabled: true, domains: ["replacement.example.test"], active: true, source: "acme", notBeforeUtc: DateTimeOffset.UnixEpoch, notAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), renewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), lastAttemptAtUtc: null, lastSucceededAtUtc: DateTimeOffset.UnixEpoch, lastFailedAtUtc: null, nextAttemptNotBeforeUtc: null, lastResult: "loaded", errorSummary: null);
        responseDomains.Clear();
        responseCertificates.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new AcmeCertificateLifecycleStatusResponse(certificateId: "home-acme", enabled: true, domains: null!, active: true, source: "acme", notBeforeUtc: DateTimeOffset.UnixEpoch, notAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), renewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), lastAttemptAtUtc: null, lastSucceededAtUtc: DateTimeOffset.UnixEpoch, lastFailedAtUtc: null, nextAttemptNotBeforeUtc: null, lastResult: "loaded", errorSummary: null));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeStatusResponse(enabled: true, directoryUrl: "https://acme.example.test/directory", useStaging: false, certificates: null!));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeCertificateLifecycleStatus(CertificateId: null!, Enabled: true, Domains: [], Active: true, Source: "acme", NotBeforeUtc: null, NotAfterUtc: null, RenewalDueAtUtc: null, LastAttemptAtUtc: null, LastSucceededAtUtc: null, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeCertificateLifecycleStatus(CertificateId: "home-acme", Enabled: true, Domains: null!, Active: true, Source: "acme", NotBeforeUtc: null, NotAfterUtc: null, RenewalDueAtUtc: null, LastAttemptAtUtc: null, LastSucceededAtUtc: null, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeCertificateLifecycleStatus(CertificateId: "home-acme", Enabled: true, Domains: [], Active: true, Source: null!, NotBeforeUtc: null, NotAfterUtc: null, RenewalDueAtUtc: null, LastAttemptAtUtc: null, LastSucceededAtUtc: null, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeCertificateLifecycleStatus(CertificateId: "home-acme", Enabled: true, Domains: [], Active: true, Source: "acme", NotBeforeUtc: null, NotAfterUtc: null, RenewalDueAtUtc: null, LastAttemptAtUtc: null, LastSucceededAtUtc: null, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: null!, ErrorSummary: null));
        AssertEx.Equal("home.example.test", responseLifecycle.Domains[0]);
        AssertEx.Equal("home-acme", directStatus.Certificates[0].CertificateId);
        AssertEx.Equal("home.example.test", directStatus.Certificates[0].Domains[0]);
        AssertEx.False(issue.Domains is string[], "ACME issue domains should not expose a mutable array.");
        AssertEx.False(write.Domains is string[], "ACME material domains should not expose a mutable array.");
        AssertEx.False(lifecycle.Domains is string[], "ACME lifecycle domains should not expose a mutable array.");
        AssertEx.False(status.Certificates is AcmeCertificateLifecycleStatus[], "ACME status certificates should not expose a mutable array.");
        AssertEx.False(apiStatus.Certificates is AcmeCertificateLifecycleStatusResponse[], "ACME API status certificates should not expose a mutable array.");
        AssertEx.False(apiStatus.Certificates[0].Domains is string[], "ACME API status domains should not expose a mutable array.");
        AssertEx.False(responseLifecycle.Domains is string[], "Direct ACME API lifecycle domains should not expose a mutable array.");
        AssertEx.False(directStatus.Certificates is AcmeCertificateLifecycleStatusResponse[], "Direct ACME API status certificates should not expose a mutable array.");
    }

    public static void AcmeLifecycleStatusConsumesActiveCertificateDates()
    {
        var notBefore = DateTimeOffset.UnixEpoch.AddDays(1);
        var notAfter = DateTimeOffset.UnixEpoch.AddDays(91);
        var configured = new ProxyAcmeConfiguredCertificateStatus("home-acme", Enabled: true, Domains: ["home.example.test"], RenewBeforeDays: 30);
        AssertEx.Throws<ArgumentException>(() => new ProxyAcmeConfiguredCertificateStatus(" ", Enabled: true, Domains: ["home.example.test"], RenewBeforeDays: 30));
        AssertEx.Throws<ArgumentException>(() => new ProxyAcmeConfiguredCertificateStatus("home-acme", Enabled: true, Domains: [], RenewBeforeDays: 30));
        AssertEx.Throws<ArgumentException>(() => new ProxyAcmeConfiguredCertificateStatus("home-acme", Enabled: true, Domains: [" "], RenewBeforeDays: 30));
        var active = AcmeCertificateLifecycleStatus.FromConfiguredCertificate(configured, new AcmeCertificateLifecycleActiveCertificate(notBefore, notAfter));
        var inactive = AcmeCertificateLifecycleStatus.FromConfiguredCertificate(configured, activeCertificate: null);
        AssertEx.True(active.Active);
        AssertEx.Equal("acme", active.Source);
        AssertEx.Equal(notBefore, active.NotBeforeUtc);
        AssertEx.Equal(notAfter, active.NotAfterUtc);
        AssertEx.Equal(notAfter.AddDays(-30), active.RenewalDueAtUtc);
        AssertEx.Equal("loaded", active.LastResult);
        AssertEx.False(inactive.Active);
        AssertEx.Equal("none", inactive.Source);
        AssertEx.Equal(null, inactive.NotBeforeUtc);
        AssertEx.Equal(null, inactive.NotAfterUtc);
        AssertEx.Equal(null, inactive.RenewalDueAtUtc);
        AssertEx.Equal("inactive", inactive.LastResult);
    }

    public static async Task AcmeRenewalStoresMaterialUnderCertsDirectory()
    {
        using var temp = TemporaryDirectory.Create();
        var issuer = new FakeIssuer(TestCertificates.CreateSelfSignedPfxBytes("home.example.test"));
        var store = CreateStore(temp.Path);
        var statusStore = new AcmeCertificateStatusStore();
        var attemptStartedAtUtc = DateTimeOffset.UnixEpoch.AddHours(9);
        var manager = CreateManager(temp.Path, store, issuer, statusStore, new ManualTimeProvider(attemptStartedAtUtc));
        await manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(false);
        var layout = AcmeCertificateMaterialStore.GetLayout(temp.Path, "acme");
        AssertEx.True(File.Exists(AcmeCertificateMaterialStore.GetPrivateKeyPfxPath(layout, "home-acme")));
        AssertEx.True(File.Exists(AcmeCertificateMaterialStore.GetCertificatePemPath(layout, "home-acme")));
        var metadataPath = AcmeCertificateMaterialStore.GetMetadataPath(layout, "home-acme");
        AssertEx.True(File.Exists(metadataPath));
        var metadata = AssertEx.NotNull(JsonSerializer.Deserialize<AcmeCertificateMetadata>(File.ReadAllText(metadataPath), SiteConfigurationParser.ReadJsonOptions));
        AssertEx.Equal(attemptStartedAtUtc, metadata.WrittenAtUtc);
        AssertEx.Equal("acme", store.Snapshot.Certificates["home-acme"].Source);
        AssertEx.True(statusStore.Get("home-acme")?.Active == true);
    }

    public static async Task LoaderLoadsStoredAcmeCertificateOnStartup()
    {
        using var temp = TemporaryDirectory.Create();
        var acmeOptions = new ProxyAcmeOptions
        {
            Enabled = true,
            TermsAccepted = true,
            Certificates = [new AcmeManagedCertificateOptions
            {
                Id = "home-acme",
                Domains = ["home.example.test"]
            }

            ]
        };
        var runtimeAcme = ProxyConfigurationRuntimeMapper.ToRuntimeAcmeOptions(acmeOptions);
        var runtimeCertificateOptions = runtimeAcme.Certificates[0];
        AcmeCertificateMaterialStore.WriteAndLoad(new AcmeCertificateMaterialWriteRequest(runtimeAcme.StoragePath, runtimeCertificateOptions.Id, runtimeCertificateOptions.Domains, temp.Path, DateTimeOffset.UnixEpoch.AddHours(10), TestCertificates.CreateSelfSignedPfxBytes("home.example.test")));
        var config = Directory.CreateDirectory(Path.Combine(temp.Path, "config")).FullName;
        File.WriteAllText(Path.Combine(config, "proxy.json"), """
            {
              "acme": {
                "enabled": true,
                "termsAccepted": true,
                "certificates": [
                  {
                    "id": "home-acme",
                    "domains": [ "home.example.test" ]
                  }
                ]
              }
            }
            """);
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        AssertEx.Equal("acme", ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Certificates["home-acme"].Source);
    }

    public static async Task FailedAcmeRenewalPreservesCurrentActiveCertificate()
    {
        using var temp = TemporaryDirectory.Create();
        var store = CreateStore(temp.Path);
        var certificateOptions = store.Snapshot.Acme.Certificates[0];
        var initial = AcmeCertificateMaterialStore.WriteAndLoad(new AcmeCertificateMaterialWriteRequest(store.Snapshot.Acme.StoragePath, certificateOptions.Id, certificateOptions.Domains, temp.Path, DateTimeOffset.UnixEpoch.AddHours(11), TestCertificates.CreateSelfSignedPfxBytes("home.example.test", validDays: 10)));
        store.Replace(store.Snapshot.WithCertificates(new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase) { ["home-acme"] = initial }));
        var originalThumbprint = initial.Certificate.Thumbprint;
        var statusStore = new AcmeCertificateStatusStore();
        var manager = CreateManager(temp.Path, store, new FakeIssuer("issuer failed"), statusStore);
        await manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(false);
        AssertEx.Equal(originalThumbprint, store.Snapshot.Certificates["home-acme"].Certificate.Thumbprint);
        AssertEx.Equal("failed", AssertEx.NotNull(statusStore.Get("home-acme")).LastResult);
    }

    public static async Task AcmeStatusProjectionDoesNotExposePrivateMaterial()
    {
        using var temp = TemporaryDirectory.Create();
        var store = CreateStore(temp.Path);
        var statusStore = new AcmeCertificateStatusStore();
        var manager = CreateManager(temp.Path, store, new FakeIssuer(TestCertificates.CreateSelfSignedPfxBytes("home.example.test")), statusStore);
        await manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(false);
        var controller = new ProxyAcmeController(new ProxyAcmeAdministrationService(CreateStatusReader(store, statusStore)));
        var result = controller.Status();
        var ok = (OkObjectResult)AssertEx.NotNull(result.Result);
        var status = (AcmeStatusResponse)AssertEx.NotNull(ok.Value);
        var text = status.ToString();
        AssertEx.False(text.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(text.Contains("current.pfx", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(text.Contains(temp.Path, StringComparison.OrdinalIgnoreCase));
        AssertEx.False(status.Certificates is AcmeCertificateLifecycleStatusResponse[], "ACME API status certificates should not expose a mutable array.");
        AssertEx.False(status.Certificates[0].Domains is string[], "ACME API status domains should not expose a mutable array.");
    }

    public static void AcmeStatusSnapshotReaderProjectsSourceState()
    {
        var notBefore = DateTimeOffset.UnixEpoch.AddDays(1);
        var notAfter = DateTimeOffset.UnixEpoch.AddDays(91);
        var lifecycle = new AcmeCertificateLifecycleStatus("home-acme", true, ["home.example.test"], true, "acme", notBefore, notAfter, notAfter.AddDays(-30), null, null, null, null, "loaded", null);
        var configuredDomains = new List<string>
        {
            "home.example.test"
        };
        var configured = new ProxyAcmeConfiguredCertificateStatus("home-acme", true, configuredDomains, 30);
        var configuredCertificates = new List<ProxyAcmeConfiguredCertificateStatus>
        {
            configured
        };
        var runtimeSource = new ProxyAcmeRuntimeCertificateSource("home-acme", "home-acme", "acme", notBefore, notAfter);
        var runtimeSources = new List<ProxyAcmeRuntimeCertificateSource>
        {
            runtimeSource
        };
        var reader = new ProxyAcmeStatusSnapshotReader(new FixedAcmeStatusConfigurationSource(new ProxyAcmeStatusConfigurationSourceSnapshot(true, "https://acme.example.test/directory", true, configuredCertificates, runtimeSources)), new FixedAcmeCertificateLifecycleStatusSource([lifecycle]));
        configuredDomains[0] = "replacement.example.test";
        configuredCertificates[0] = new ProxyAcmeConfiguredCertificateStatus("replacement", true, ["replacement.example.test"], 10);
        runtimeSources[0] = new ProxyAcmeRuntimeCertificateSource("replacement", "replacement", "manualPfx", notBefore.AddDays(1), notAfter.AddDays(1));
        configuredDomains.Clear();
        configuredCertificates.Clear();
        runtimeSources.Clear();
        var result = reader.ReadSnapshot();
        var statuses = reader.GetLifecycleStatuses();
        var missingReader = new ProxyAcmeStatusSnapshotReader(new FixedAcmeStatusConfigurationSource(null), new FixedAcmeCertificateLifecycleStatusSource([]));
        var missing = missingReader.ReadSnapshot();
        AssertEx.True(result is ProxyAcmeStatusSnapshotReadResult.AvailableResult);
        var projected = ((ProxyAcmeStatusSnapshotReadResult.AvailableResult)result).Snapshot;
        AssertEx.True(projected.Enabled);
        AssertEx.Equal("https://acme.example.test/directory", projected.DirectoryUrl);
        AssertEx.True(projected.UseStaging);
        AssertEx.Equal(1, projected.Certificates.Count);
        AssertEx.Equal("home-acme", projected.Certificates[0].Id);
        AssertEx.Equal("home.example.test", projected.Certificates[0].Domains[0]);
        AssertEx.Equal(30, projected.Certificates[0].RenewBeforeDays);
        AssertEx.True(projected.RuntimeCertificates.ContainsKey("home-acme"));
        AssertEx.Equal("acme", projected.RuntimeCertificates["home-acme"].Source);
        AssertEx.Equal(notBefore, projected.RuntimeCertificates["home-acme"].NotBeforeUtc);
        AssertEx.Equal(notAfter, projected.RuntimeCertificates["home-acme"].NotAfterUtc);
        AssertEx.False(projected.Certificates is ProxyAcmeConfiguredCertificateStatus[], "ACME status snapshot certificates should not expose a mutable array.");
        AssertEx.False(projected.Certificates[0].Domains is string[], "ACME configured certificate domains should not expose a mutable array.");
        var runtimeStatusDictionary = new Dictionary<string, ProxyAcmeRuntimeCertificateStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["home-acme"] = new ProxyAcmeRuntimeCertificateStatus("home-acme", "acme", notBefore, notAfter)
        };
        var directSnapshot = new ProxyAcmeStatusSnapshot(Enabled: true, DirectoryUrl: "https://acme.example.test/directory", UseStaging: true, Certificates: [configured], RuntimeCertificates: runtimeStatusDictionary);
        runtimeStatusDictionary["home-acme"] = new ProxyAcmeRuntimeCertificateStatus("home-acme", "manualPfx", notBefore.AddDays(1), notAfter.AddDays(1));
        AssertEx.Equal("acme", directSnapshot.RuntimeCertificates["home-acme"].Source);
        AssertEx.Equal(1, statuses.Count);
        AssertEx.Equal(lifecycle, statuses[0]);
        AssertEx.True(missing is ProxyAcmeStatusSnapshotReadResult.MissingConfigurationResult);
    }

    public static void AcmeRuntimeCertificateStatusMapperReadsSourcesWithoutConfigurationSnapshot()
    {
        var notBefore = DateTimeOffset.UnixEpoch.AddDays(1);
        var notAfter = DateTimeOffset.UnixEpoch.AddDays(91);
        var certificates = ProxyAcmeRuntimeCertificateStatusMapper.FromSources([new ProxyAcmeRuntimeCertificateSource("HOME-ACME", "home-acme", "acme", notBefore, notAfter)]);
        AssertEx.Throws<ArgumentNullException>(() => ProxyAcmeRuntimeCertificateStatusMapper.FromSources(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyAcmeRuntimeCertificateStatusMapper.FromSources([null!]));
        AssertEx.Throws<ArgumentException>(() => new ProxyAcmeRuntimeCertificateSource(" ", "home-acme", "acme", notBefore, notAfter));
        AssertEx.Throws<ArgumentException>(() => new ProxyAcmeRuntimeCertificateSource("home-acme", " ", "acme", notBefore, notAfter));
        AssertEx.Throws<ArgumentException>(() => new ProxyAcmeRuntimeCertificateSource("home-acme", "home-acme", " ", notBefore, notAfter));
        AssertEx.Throws<ArgumentException>(() => new ProxyAcmeRuntimeCertificateStatus(" ", "acme", notBefore, notAfter));
        AssertEx.Throws<ArgumentException>(() => new ProxyAcmeRuntimeCertificateStatus("home-acme", " ", notBefore, notAfter));
        AssertEx.True(certificates.ContainsKey("home-acme"));
        var certificate = certificates["home-acme"];
        AssertEx.Equal("home-acme", certificate.Id);
        AssertEx.Equal("acme", certificate.Source);
        AssertEx.Equal(notBefore, certificate.NotBeforeUtc);
        AssertEx.Equal(notAfter, certificate.NotAfterUtc);
    }

    public static void AcmeStatusConfigurationSourceMapperReadsRuntimeFactsWithoutSnapshot()
    {
        using var temp = TemporaryDirectory.Create();
        using var certificate = X509CertificateLoader.LoadPkcs12(TestCertificates.CreateSelfSignedPfxBytes("home.example.test"), ReadOnlySpan<char>.Empty, X509KeyStorageFlags.UserKeySet);
        var store = CreateStore(temp.Path);
        var snapshot = store.Snapshot.WithCertificates(new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase) { ["home-acme"] = RuntimeCertificate("home-acme", certificate, "acme") });
        var runtimeCertificates = new Dictionary<string, RuntimeCertificate>(snapshot.Certificates, StringComparer.OrdinalIgnoreCase);
        var source = ProxyAcmeStatusConfigurationSourceMapper.FromSources(snapshot.Acme, runtimeCertificates.Select(static certificate => certificate));
        AssertEx.Throws<ArgumentNullException>(() => ProxyAcmeStatusConfigurationSourceMapper.FromSources(null!, runtimeCertificates));
        AssertEx.Throws<ArgumentNullException>(() => ProxyAcmeStatusConfigurationSourceMapper.FromSources(snapshot.Acme, null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyAcmeStatusConfigurationSourceMapper.FromSources(AcmeWithCertificates([null!]), runtimeCertificates));
        AssertEx.Throws<ArgumentNullException>(() => ProxyAcmeStatusConfigurationSourceMapper.FromSources(snapshot.Acme, new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase) { ["broken"] = null! }));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyAcmeStatusConfigurationSourceSnapshot(snapshot.Acme.Enabled, snapshot.Acme.DirectoryUrl, snapshot.Acme.UseStaging, [null!], source.RuntimeCertificates));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyAcmeStatusConfigurationSourceSnapshot(snapshot.Acme.Enabled, snapshot.Acme.DirectoryUrl, snapshot.Acme.UseStaging, source.Certificates, [null!]));
        runtimeCertificates.Clear();
        AssertEx.True(source.Enabled);
        AssertEx.Equal(snapshot.Acme.DirectoryUrl, source.DirectoryUrl);
        AssertEx.Equal(snapshot.Acme.UseStaging, source.UseStaging);
        AssertEx.Equal(1, source.Certificates.Count);
        AssertEx.Equal("home-acme", source.Certificates[0].Id);
        AssertEx.Equal("home.example.test", source.Certificates[0].Domains[0]);
        AssertEx.Equal(30, source.Certificates[0].RenewBeforeDays);
        AssertEx.Equal(1, source.RuntimeCertificates.Count);
        AssertEx.Equal("home-acme", source.RuntimeCertificates[0].Key);
        AssertEx.Equal("home-acme", source.RuntimeCertificates[0].Id);
        AssertEx.Equal("acme", source.RuntimeCertificates[0].Source);
        AssertEx.Equal(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()), source.RuntimeCertificates[0].NotBeforeUtc);
        AssertEx.Equal(new DateTimeOffset(certificate.NotAfter.ToUniversalTime()), source.RuntimeCertificates[0].NotAfterUtc);
        AssertEx.False(source.Certificates is ProxyAcmeConfiguredCertificateStatus[], "ACME status source certificates should not expose a mutable array.");
        AssertEx.False(source.RuntimeCertificates is ProxyAcmeRuntimeCertificateSource[], "ACME runtime certificate sources should not expose a mutable array.");
        AssertEx.False(source.Certificates[0].Domains is string[], "ACME status source domains should not expose a mutable array.");
        RuntimeAcmeOptions AcmeWithCertificates(IReadOnlyList<RuntimeAcmeCertificateOptions> certificates)
        {
            return new RuntimeAcmeOptions(snapshot.Acme.Enabled, snapshot.Acme.UseStaging, snapshot.Acme.DirectoryUrl, snapshot.Acme.ContactEmails, snapshot.Acme.TermsAccepted, snapshot.Acme.StoragePath, snapshot.Acme.RenewBeforeDays, snapshot.Acme.CheckIntervalMinutes, snapshot.Acme.RetryAfterMinutes, certificates);
        }
    }

    public static async Task AcmeRenewalAvoidsTightRetryLoopAfterFailure()
    {
        using var temp = TemporaryDirectory.Create();
        var issuer = new FakeIssuer("issuer failed");
        var statusStore = new AcmeCertificateStatusStore();
        var now = DateTimeOffset.UtcNow;
        var manager = CreateManager(temp.Path, CreateStore(temp.Path), issuer, statusStore, new ManualTimeProvider(now));
        await manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(false);
        await manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(false);
        AssertEx.Equal(1, issuer.Calls);
        AssertEx.True(statusStore.Get("home-acme")?.NextAttemptNotBeforeUtc > now);
    }

    public static void AcmeRenewalScheduleUsesDisabledBackoffWithoutActiveConfig()
    {
        var delay = new AcmeRenewalSchedulePolicy().ResolveDelay(AcmeRenewalScheduleInputReadResult.MissingConfiguration);
        AssertEx.Equal(TimeSpan.FromHours(12), delay);
    }

    public static void AcmeRenewalScheduleClampsConfiguredInterval()
    {
        using var temp = TemporaryDirectory.Create();
        var policy = new AcmeRenewalSchedulePolicy();
        var belowMinimum = AcmeRenewalScheduleInputMapper.FromSource(AcmeRenewalScheduleSourceMapper.FromSource(CreateStore(temp.Path, checkIntervalMinutes: 1).Snapshot.Acme));
        var aboveMaximum = AcmeRenewalScheduleInputMapper.FromSource(AcmeRenewalScheduleSourceMapper.FromSource(CreateStore(temp.Path, checkIntervalMinutes: 2000).Snapshot.Acme));
        AssertEx.Equal(TimeSpan.FromMinutes(5), policy.ResolveDelay(AcmeRenewalScheduleInputReadResult.Available(belowMinimum)));
        AssertEx.Equal(TimeSpan.FromMinutes(1440), policy.ResolveDelay(AcmeRenewalScheduleInputReadResult.Available(aboveMaximum)));
    }

    public static void AcmeRenewalScheduleInputMapperConsumesSourceWithoutRuntimeConfiguration()
    {
        var input = AcmeRenewalScheduleInputMapper.FromSource(new AcmeRenewalScheduleSource(Enabled: true, CheckIntervalMinutes: 17));
        AssertEx.True(input.Enabled);
        AssertEx.Equal(17, input.CheckIntervalMinutes);
    }

    public static void AcmeRenewalScheduleRejectsNullInputs()
    {
        var policy = new AcmeRenewalSchedulePolicy();
        AssertEx.Throws<ArgumentNullException>(() => AcmeRenewalScheduleInputMapper.FromSource(null!));
        AssertEx.Throws<ArgumentNullException>(() => policy.ResolveDelay(null!));
    }

    public static void AcmeRenewalScheduleSourceReadsNarrowActiveInput()
    {
        using var temp = TemporaryDirectory.Create();
        var source = new ProxyConfigurationAcmeRenewalScheduleInputSource(CreateStore(temp.Path, checkIntervalMinutes: 17));
        var result = source.ReadInput();
        AssertEx.True(result is AcmeRenewalScheduleInputReadResult.AvailableResult);
        var input = ((AcmeRenewalScheduleInputReadResult.AvailableResult)result).Input;
        AssertEx.True(input.Enabled);
        AssertEx.Equal(17, input.CheckIntervalMinutes);
    }

    public static void AcmeRenewalConfigurationSourceProjectsRenewalInput()
    {
        using var temp = TemporaryDirectory.Create();
        var store = CreateStore(temp.Path);
        var source = new ProxyConfigurationAcmeRenewalConfigurationSource(store);
        var result = source.ReadInput();
        AssertEx.True(result is AcmeRenewalConfigurationInputReadResult.AvailableResult);
        var input = ((AcmeRenewalConfigurationInputReadResult.AvailableResult)result).Input;
        AssertEx.True(input.Enabled);
        AssertEx.Equal("acme", input.StoragePath);
        AssertEx.Equal(60, input.RetryAfterMinutes);
        AssertEx.Equal(1, input.Certificates.Count);
        AssertEx.Equal("home-acme", input.Certificates[0].Id);
        AssertEx.Equal("home.example.test", input.Certificates[0].Domains[0]);
        AssertEx.Equal(30, input.Certificates[0].RenewBeforeDays);
    }

    public static void AcmeRenewalConfigurationSourceMapperAttachesOnlyAcmeActiveCertificates()
    {
        using var certificate = X509CertificateLoader.LoadPkcs12(TestCertificates.CreateSelfSignedPfxBytes("home.example.test"), ReadOnlySpan<char>.Empty, X509KeyStorageFlags.UserKeySet);
        var acme = new RuntimeAcmeOptions(Enabled: true, UseStaging: false, DirectoryUrl: "https://acme.example.test/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true, StoragePath: "acme", RenewBeforeDays: 30, CheckIntervalMinutes: 60, RetryAfterMinutes: 15, Certificates: [new RuntimeAcmeCertificateOptions("home-acme", Enabled: true, Domains: ["home.example.test"], RenewBeforeDays: 20), new RuntimeAcmeCertificateOptions("manual-cert", Enabled: true, Domains: ["manual.example.test"], RenewBeforeDays: 25)]);
        Dictionary<string, RuntimeCertificate> runtimeCertificates = new(StringComparer.OrdinalIgnoreCase)
        {
            ["home-acme"] = RuntimeCertificate("home-acme", certificate, "acme"),
            ["manual-cert"] = RuntimeCertificate("manual-cert", certificate, "manualPfx")
        };
        var source = AcmeRenewalConfigurationSourceMapper.FromSources(acme, runtimeCertificates.Select(static certificate => certificate));
        AssertEx.Throws<ArgumentNullException>(() => AcmeRenewalConfigurationSourceMapper.FromSources(null!, runtimeCertificates));
        AssertEx.Throws<ArgumentNullException>(() => AcmeRenewalConfigurationSourceMapper.FromSources(acme, null!));
        AssertEx.Throws<ArgumentNullException>(() => AcmeRenewalConfigurationSourceMapper.FromSources(AcmeWithCertificates([null!]), runtimeCertificates));
        AssertEx.Throws<ArgumentNullException>(() => AcmeRenewalConfigurationSourceMapper.FromSources(acme, new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase) { ["broken"] = null! }));
        runtimeCertificates.Clear();
        AssertEx.True(source.Enabled);
        AssertEx.Equal("acme", source.StoragePath);
        AssertEx.Equal("https://acme.example.test/directory", source.DirectoryUrl);
        AssertEx.Equal(15, source.RetryAfterMinutes);
        AssertEx.Equal(2, source.Certificates.Count);
        AssertEx.NotNull(source.Certificates[0].ActiveCertificate);
        AssertEx.Equal(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()), source.Certificates[0].ActiveCertificate!.NotBeforeUtc);
        AssertEx.Equal(new DateTimeOffset(certificate.NotAfter.ToUniversalTime()), source.Certificates[0].ActiveCertificate!.NotAfterUtc);
        AssertEx.Equal("home.example.test", source.Certificates[0].Domains[0]);
        AssertEx.Equal(20, source.Certificates[0].RenewBeforeDays);
        AssertEx.Equal("manual-cert", source.Certificates[1].Id);
        AssertEx.Equal(null, source.Certificates[1].ActiveCertificate);
        AssertEx.False(source.Certificates is AcmeRenewalCertificateSource[], "ACME renewal source mapper certificates should not expose a mutable array.");
        AssertEx.False(source.Certificates[0].Domains is string[], "ACME renewal source mapper domains should not expose a mutable array.");
        RuntimeAcmeOptions AcmeWithCertificates(IReadOnlyList<RuntimeAcmeCertificateOptions> certificates)
        {
            return new RuntimeAcmeOptions(acme.Enabled, acme.UseStaging, acme.DirectoryUrl, acme.ContactEmails, acme.TermsAccepted, acme.StoragePath, acme.RenewBeforeDays, acme.CheckIntervalMinutes, acme.RetryAfterMinutes, certificates);
        }
    }

    public static void AcmeRenewalConfigurationInputMapperConsumesSourceSetWithoutRuntimeConfiguration()
    {
        var contactEmails = new List<string>
        {
            "ops@example.test"
        };
        var certificateDomains = new List<string>
        {
            "home.example.test"
        };
        var activeCertificate = new AcmeRenewalActiveCertificate(DateTimeOffset.UnixEpoch.AddDays(-10), DateTimeOffset.UnixEpoch.AddDays(20));
        var certificateSource = new AcmeRenewalCertificateSource("home-acme", Enabled: true, Domains: certificateDomains, RenewBeforeDays: 20, activeCertificate);
        var certificates = new List<AcmeRenewalCertificateSource>
        {
            certificateSource
        };
        var source = new AcmeRenewalConfigurationSourceSet(Enabled: true, StoragePath: "acme", DirectoryUrl: "https://acme.example.test/directory", ContactEmails: contactEmails, TermsAccepted: true, RetryAfterMinutes: 15, Certificates: certificates);
        var input = AcmeRenewalConfigurationInputMapper.FromSources(source);
        contactEmails[0] = "security@example.test";
        certificateDomains[0] = "replacement.example.test";
        certificates[0] = new AcmeRenewalCertificateSource("replacement", Enabled: false, Domains: ["replacement.example.test"], RenewBeforeDays: 10, ActiveCertificate: null);
        contactEmails.Clear();
        certificateDomains.Clear();
        certificates.Clear();
        AssertEx.Throws<ArgumentException>(() => new AcmeRenewalConfigurationSourceSet(Enabled: true, StoragePath: " ", DirectoryUrl: "https://acme.example.test/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true, RetryAfterMinutes: 15, Certificates: []));
        AssertEx.Throws<ArgumentException>(() => new AcmeRenewalConfigurationSourceSet(Enabled: true, StoragePath: "acme", DirectoryUrl: " ", ContactEmails: ["ops@example.test"], TermsAccepted: true, RetryAfterMinutes: 15, Certificates: []));
        AssertEx.Throws<ArgumentException>(() => new AcmeRenewalConfigurationSourceSet(Enabled: true, StoragePath: "acme", DirectoryUrl: "https://acme.example.test/directory", ContactEmails: [" "], TermsAccepted: true, RetryAfterMinutes: 15, Certificates: []));
        AssertEx.Throws<ArgumentException>(() => new AcmeRenewalCertificateSource(" ", Enabled: true, Domains: ["home.example.test"], RenewBeforeDays: 20, ActiveCertificate: null));
        AssertEx.Throws<ArgumentException>(() => new AcmeRenewalCertificateSource("home-acme", Enabled: true, Domains: [], RenewBeforeDays: 20, ActiveCertificate: null));
        AssertEx.Throws<ArgumentException>(() => new AcmeRenewalCertificateSource("home-acme", Enabled: true, Domains: [" "], RenewBeforeDays: 20, ActiveCertificate: null));
        AssertEx.Throws<ArgumentException>(() => new AcmeRenewalConfigurationInput(Enabled: true, StoragePath: " ", DirectoryUrl: "https://acme.example.test/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true, RetryAfterMinutes: 15, Certificates: []));
        AssertEx.Throws<ArgumentException>(() => new AcmeRenewalCertificateInput(" ", Enabled: true, Domains: ["home.example.test"], RenewBeforeDays: 20, ActiveCertificate: null));
        AssertEx.Throws<ArgumentException>(() => new AcmeRenewalCertificateInput("home-acme", Enabled: true, Domains: [], RenewBeforeDays: 20, ActiveCertificate: null));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeRenewalConfigurationSourceSet(Enabled: true, StoragePath: "acme", DirectoryUrl: "https://acme.example.test/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true, RetryAfterMinutes: 15, Certificates: [null!]));
        AssertEx.Throws<ArgumentNullException>(() => new AcmeRenewalConfigurationInput(Enabled: true, StoragePath: "acme", DirectoryUrl: "https://acme.example.test/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true, RetryAfterMinutes: 15, Certificates: [null!]));
        AssertEx.True(input.Enabled);
        AssertEx.Equal("acme", input.StoragePath);
        AssertEx.Equal("https://acme.example.test/directory", input.DirectoryUrl);
        AssertEx.Equal("ops@example.test", source.ContactEmails[0]);
        AssertEx.Equal("home-acme", source.Certificates[0].Id);
        AssertEx.Equal("home.example.test", source.Certificates[0].Domains[0]);
        AssertEx.Equal("ops@example.test", input.ContactEmails[0]);
        AssertEx.True(input.TermsAccepted);
        AssertEx.Equal(15, input.RetryAfterMinutes);
        AssertEx.Equal(1, input.Certificates.Count);
        AssertEx.Equal("home-acme", input.Certificates[0].Id);
        AssertEx.Equal("home.example.test", input.Certificates[0].Domains[0]);
        AssertEx.Equal(20, input.Certificates[0].RenewBeforeDays);
        AssertEx.Equal(activeCertificate, input.Certificates[0].ActiveCertificate);
        AssertEx.False(source.ContactEmails is string[], "ACME renewal source contacts should not expose a mutable array.");
        AssertEx.False(source.Certificates is AcmeRenewalCertificateSource[], "ACME renewal source certificates should not expose a mutable array.");
        AssertEx.False(input.ContactEmails is string[], "ACME renewal input contacts should not expose a mutable array.");
        AssertEx.False(input.Certificates is AcmeRenewalCertificateInput[], "ACME renewal input certificates should not expose a mutable array.");
        AssertEx.False(input.Certificates[0].Domains is string[], "ACME renewal input domains should not expose a mutable array.");
    }

    public static void AcmeRenewalConfigurationInputMapperRejectsNullSourceSet()
    {
        AssertEx.Throws<ArgumentNullException>(() => AcmeRenewalConfigurationInputMapper.FromSources(null!));
    }

    private static Http1RequestHead Request(string method, string path)
    {
        return new Http1RequestHead(method, path, path, "HTTP/1.1", "home.example.test", Http1RequestFraming.None, []);
    }

    private static ProxyConfigurationStore CreateStore(string dataDirectory, int checkIntervalMinutes = 60)
    {
        var options = new ProxyOperationalOptions
        {
            Acme = new ProxyAcmeOptions
            {
                Enabled = true,
                TermsAccepted = true,
                RetryAfterMinutes = 60,
                CheckIntervalMinutes = checkIntervalMinutes,
                Certificates = [new AcmeManagedCertificateOptions
                {
                    Id = "home-acme",
                    Domains = ["home.example.test"],
                    RenewBeforeDays = 30
                }

                ]
            }
        };
        var snapshot = ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(new ProxyOptions(), options, ProxyAdminSecurityTokenPolicy.Resolve(options.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UtcNow, Path.Combine(dataDirectory, "config", "sites"), [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout(dataDirectory, Path.Combine(dataDirectory, "config"), Path.Combine(dataDirectory, "config", "sites"), Path.Combine(dataDirectory, "logs"), Path.Combine(dataDirectory, "certs"), Path.Combine(dataDirectory, "state"), Path.Combine(dataDirectory, "config", "proxy.json")), [], [], []));
        var store = new ProxyConfigurationStore();
        store.Replace(snapshot);
        return store;
    }

    private static RuntimeCertificate RuntimeCertificate(string id, X509Certificate2 certificate, string source)
    {
        return new RuntimeCertificate(id, $"{id}.pfx", "pfx", HasConfiguredPassword: false, certificate, source, [id]);
    }

    private static AcmeCertificateManager CreateManager(string dataDirectory, ProxyConfigurationStore store, IAcmeCertificateIssuer issuer, AcmeCertificateStatusStore statusStore, TimeProvider? timeProvider = null)
    {
        return new AcmeCertificateManager(new ProxyConfigurationAcmeRenewalConfigurationSource(store), new ProxyConfigurationAcmeCertificateActivator(store, store), new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory }), issuer, new AcmeCertificateMaterialWriter(), new AcmeChallengeStore(), statusStore, timeProvider ?? new ManualTimeProvider(DateTimeOffset.UtcNow), new ProxyMetrics(), SilentAcmeCertificateRenewalEventSink.Instance);
    }

    private static ProxyAcmeStatusSnapshotReader CreateStatusReader(ProxyConfigurationStore store, AcmeCertificateStatusStore statusStore)
    {
        return new ProxyAcmeStatusSnapshotReader(new ProxyAcmeStatusConfigurationSource(store), new ProxyAcmeCertificateLifecycleStatusSource(statusStore));
    }

    private static ProxyConfigurationLoader CreateLoader(string dataDirectory)
    {
        var provider = new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory });
        return new ProxyConfigurationLoader(provider, new ProxyDataDirectoryBootstrapper(provider), new SiteConfigurationParser(), new Mk8.Drava.Application.INF.Configuration.ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, TimeProvider.System);
    }

    private sealed class FakeIssuer : IAcmeCertificateIssuer
    {
        private readonly byte[]? _pfxBytes;
        private readonly string? _error;
        public FakeIssuer(byte[] pfxBytes)
        {
            _pfxBytes = pfxBytes;
        }

        public FakeIssuer(string error)
        {
            _error = error;
        }

        public int Calls { get; private set; }

        public ValueTask<AcmeCertificateIssueResult> IssueAsync(AcmeCertificateIssueRequest request, AcmeChallengeStore challengeStore, CancellationToken cancellationToken)
        {
            _ = request;
            _ = challengeStore;
            _ = cancellationToken;
            Calls++;
            return ValueTask.FromResult(_pfxBytes is not null ? AcmeCertificateIssueResult.Issued(_pfxBytes) : AcmeCertificateIssueResult.Failed(_error ?? "failed"));
        }
    }

    private sealed class SilentAcmeCertificateRenewalEventSink : IAcmeCertificateRenewalEventSink
    {
        public static SilentAcmeCertificateRenewalEventSink Instance { get; } = new();

        private SilentAcmeCertificateRenewalEventSink()
        {
        }

        public void RenewalFailed(string certificateId, string? errorSummary)
        {
        }
    }

    private sealed class FixedAcmeStatusConfigurationSource : IProxyAcmeStatusConfigurationSource
    {
        private readonly ProxyAcmeStatusConfigurationSourceSnapshot? _snapshot;
        public FixedAcmeStatusConfigurationSource(ProxyAcmeStatusConfigurationSourceSnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public ProxyAcmeStatusConfigurationSourceReadResult Read()
        {
            return _snapshot is null ? ProxyAcmeStatusConfigurationSourceReadResult.MissingConfiguration : ProxyAcmeStatusConfigurationSourceReadResult.Available(_snapshot);
        }
    }

    private sealed class FixedAcmeCertificateLifecycleStatusSource : IProxyAcmeCertificateLifecycleStatusSource
    {
        private readonly IReadOnlyList<AcmeCertificateLifecycleStatus> _statuses;
        public FixedAcmeCertificateLifecycleStatusSource(IReadOnlyList<AcmeCertificateLifecycleStatus> statuses)
        {
            _statuses = statuses;
        }

        public IReadOnlyList<AcmeCertificateLifecycleStatus> GetLifecycleStatuses()
        {
            return _statuses;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public ManualTimeProvider(DateTimeOffset utcNow)
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-acme-tests-{Guid.NewGuid():N}");
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
