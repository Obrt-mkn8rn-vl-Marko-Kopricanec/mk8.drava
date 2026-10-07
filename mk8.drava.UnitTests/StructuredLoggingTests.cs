using System.Collections;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.INF.Observability;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Observability;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Status;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class StructuredLoggingTests
{
    [Fact]
    public void ReloadInformationPreservesTypedValuesAndMessage()
    {
        var capture = new CapturingLogger<ProxyConfigurationReloadLogger>();
        new ProxyConfigurationReloadLogger(capture).Loaded(23, "/private/config");
#pragma warning disable HLQ005 // xUnit verifies exactly one entry; First would weaken the assertion.
        var entry = Assert.Single(capture.Entries);
#pragma warning restore HLQ005
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(10011, entry.EventId.Id);
        Assert.Equal(23, Assert.IsType<int>(entry.Values["Version"]));
        Assert.Equal("/private/config", entry.Values["SourcePath"]);
        Assert.Equal("Proxy configuration version {Version} loaded from {SourcePath}", entry.Values["{OriginalFormat}"]);
        Assert.Equal("Proxy configuration version 23 loaded from /private/config", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public void DisabledReloadLoggingDoesNotEnumerateErrors()
    {
        var capture = new CapturingLogger<ProxyConfigurationReloadLogger>(enabled: false);
        new ProxyConfigurationReloadLogger(capture).LoadFailed("/private/config", new UnreadableErrors());
        Assert.Empty(capture.Entries);
    }

    [Fact]
    public void ReloadWarningRetainsJoinedValuesAndLevel()
    {
        var capture = new CapturingLogger<ProxyConfigurationReloadLogger>();
        new ProxyConfigurationReloadLogger(capture).LoadFailed("/private/config", ["first", "second"]);
#pragma warning disable HLQ005 // xUnit verifies exactly one entry; First would weaken the assertion.
        var entry = Assert.Single(capture.Entries);
#pragma warning restore HLQ005
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(10010, entry.EventId.Id);
        Assert.Equal("first; second", entry.Values["Errors"]);
        Assert.Equal("Proxy configuration reload failed from /private/config: first; second", entry.Message);
    }

    [Fact]
    public void RenewalLoggingKeepsNullStructuredValue()
    {
        var capture = new CapturingLogger<AcmeCertificateRenewalLogger>();
        new AcmeCertificateRenewalLogger(capture).RenewalFailed("service", null);
#pragma warning disable HLQ005 // xUnit verifies exactly one entry; First would weaken the assertion.
        var entry = Assert.Single(capture.Entries);
#pragma warning restore HLQ005
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(10008, entry.EventId.Id);
        Assert.Equal("service", entry.Values["CertificateId"]);
        Assert.True(entry.Values.ContainsKey("ErrorSummary"));
        Assert.Null(entry.Values["ErrorSummary"]);
    }

    [Fact]
    public void AccessLoggingKeepsTypesAndRedactsQueryInLogState()
    {
        var metrics = new ProxyMetrics();
        var diagnostics = new RecentRequestDiagnosticsStore(metrics);
        var persistence = new CapturingPersistence();
        var capture = new CapturingLogger<AccessLogEmitter>();
        var context = new ProxyRequestContext("request", "public", "tls", null, 42, TimeProvider.System, "http2");
        context.SetRequest("GET", "service.site.test", "/items?private=secret#fragment", null);
        context.RecordForwardingResult(ForwardingResult.Success(true, true, 201), true);
        new AccessLogEmitter(diagnostics, metrics, capture, persistence).Complete(context, true, 10);
#pragma warning disable HLQ005 // xUnit verifies exactly one entry; First would weaken the assertion.
        var entry = Assert.Single(capture.Entries);
#pragma warning restore HLQ005
        Assert.Equal(10007, entry.EventId.Id);
        Assert.Equal("/items", entry.Values["TargetPath"]);
        Assert.DoesNotContain("secret", entry.Message, StringComparison.Ordinal);
        Assert.Equal(201, Assert.IsType<int>(entry.Values["StatusCode"]));
        Assert.Equal(42, Assert.IsType<int>(entry.Values["ConfigVersion"]));
        Assert.True(Assert.IsType<bool>(entry.Values["ResponseStarted"]));
        Assert.True(Assert.IsType<bool>(entry.Values["KeepAlive"]));
        Assert.IsType<long>(entry.Values["DurationMilliseconds"]);
        Assert.Null(entry.Values["ClientEndpoint"]);
#pragma warning disable HLQ005 // xUnit verifies exactly one entry; First would weaken the assertion.
        Assert.Single(persistence.Entries);
#pragma warning restore HLQ005
#pragma warning disable HLQ005 // xUnit verifies exactly one entry; First would weaken the assertion.
        Assert.Single(diagnostics.Recent(10));
#pragma warning restore HLQ005
    }

    [Fact]
    public void DisabledLoggerPreservesAccessPersistenceAndDiagnostics()
    {
        var metrics = new ProxyMetrics();
        var diagnostics = new RecentRequestDiagnosticsStore(metrics);
        var persistence = new CapturingPersistence();
        var capture = new CapturingLogger<AccessLogEmitter>(enabled: false);
        var context = new ProxyRequestContext("request", "public", "tls", null, 42, TimeProvider.System);
        new AccessLogEmitter(diagnostics, metrics, capture, persistence).Complete(context, true, 10);
        Assert.Empty(capture.Entries);
#pragma warning disable HLQ005 // xUnit verifies exactly one entry; First would weaken the assertion.
        Assert.Single(persistence.Entries);
#pragma warning restore HLQ005
#pragma warning disable HLQ005 // xUnit verifies exactly one entry; First would weaken the assertion.
        Assert.Single(diagnostics.Recent(10));
#pragma warning restore HLQ005
    }

    private sealed class CapturingPersistence : IProxyLogPersistenceStore
    {
        internal List<ProxyAccessLogEntry> Entries { get; } = [];
        public void WriteAccess(ProxyAccessLogEntry entry) => Entries.Add(entry);
        public void WriteAdminAudit(ProxyAdminAuditEvent auditEvent) { }
        public ProxyLogPersistenceStatus GetStatus() => ProxyLogPersistenceStatus.Unknown;
    }

    private sealed class CapturingLogger<T>(bool enabled = true) : ILogger<T>
    {
        internal List<CapturedEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var values = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
            Entries.Add(new CapturedEntry(logLevel, eventId, formatter(state, exception), exception,
                values.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)));
        }
    }

    private sealed record CapturedEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception, Dictionary<string, object?> Values);

    private sealed class UnreadableErrors : IReadOnlyList<string>
    {
        public int Count => 1;
        public string this[int index] => throw new InvalidOperationException("Disabled logging must not read errors.");
        IEnumerator<string> IEnumerable<string>.GetEnumerator() => throw new InvalidOperationException("Disabled logging must not enumerate errors.");
        IEnumerator IEnumerable.GetEnumerator() => throw new InvalidOperationException("Disabled logging must not enumerate errors.");
    }
}
