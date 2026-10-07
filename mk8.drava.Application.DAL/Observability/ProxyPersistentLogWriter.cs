using Mk8.Drava.Application.BLL.ControlPlane.Status;
using System.Text;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.Observability;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.DAL.Observability;
public sealed partial class ProxyPersistentLogWriter : IProxyLogPersistenceStore
{
    private const int MaxTextLength = 256;
    private const int MaxPathLength = 512;
    private static readonly Encoding LogEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };
    private readonly IMdravaDataDirectoryProvider _dataDirectoryProvider;
    private readonly IProxyLogPersistenceSettingsReader _settingsReader;
    private readonly ILogger<ProxyPersistentLogWriter> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _accessGate = new();
    private readonly object _auditGate = new();
    private readonly Lock _statusGate = new();
    private DateTimeOffset? _lastSuccessfulWriteAtUtc;
    private ProxyLogPersistenceFailureStatus? _lastWriteFailure;
    public ProxyPersistentLogWriter(IMdravaDataDirectoryProvider dataDirectoryProvider, IProxyLogPersistenceSettingsReader settingsReader, ILogger<ProxyPersistentLogWriter> logger, TimeProvider timeProvider)
    {
        _dataDirectoryProvider = dataDirectoryProvider;
        _settingsReader = settingsReader;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void WriteAccess(ProxyAccessLogEntry accessEntry)
    {
        ArgumentNullException.ThrowIfNull(accessEntry);
        var settingsResult = _settingsReader.ReadLogPersistenceSettings();
        if (settingsResult is not ProxyLogPersistenceSettingsReadResult.ActiveResult active || !active.Settings.AccessLogEnabled)
        {
            return;
        }

        var settings = active.Settings;
        var entry = new
        {
            timestampUtc = accessEntry.TimestampUtc,
            kind = "access",
            requestId = SafeValue(accessEntry.RequestId),
            configVersion = accessEntry.ConfigVersion,
            listener = SafeValue(accessEntry.ListenerName),
            transport = SafeValue(accessEntry.Transport?.ToLowerInvariant()),
            protocol = SafeValue(accessEntry.Protocol),
            method = SafeValue(accessEntry.Method),
            host = SafeValue(accessEntry.Host),
            targetPath = SafeTargetPath(accessEntry.Target),
            site = SafeValue(accessEntry.SiteName),
            route = SafeValue(accessEntry.RouteName),
            action = SafeValue(accessEntry.RouteAction),
            upstream = SafeValue(accessEntry.UpstreamName),
            upstreamEndpoint = SafeValue(accessEntry.UpstreamEndpoint),
            status = accessEntry.ResponseStatusCode,
            durationMs = accessEntry.DurationMilliseconds,
            failure = SafeValue(accessEntry.FailureKind),
            responseStarted = accessEntry.ResponseStarted,
            keepAlive = accessEntry.KeepClientConnectionOpen,
            upgrade = accessEntry.IsUpgrade,
            tunnel = accessEntry.TunnelEstablished
        };
        WriteLine("access", JsonSerializer.Serialize(entry, JsonOptions), settings, _accessGate);
    }

    public void WriteAdminAudit(ProxyAdminAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var settingsResult = _settingsReader.ReadLogPersistenceSettings();
        if (settingsResult is not ProxyLogPersistenceSettingsReadResult.ActiveResult active || !active.Settings.AdminAuditEnabled)
        {
            return;
        }

        var settings = active.Settings;
        var entry = new
        {
            timestampUtc = auditEvent.TimestampUtc,
            kind = "admin_audit",
            method = SafeValue(auditEvent.Method),
            path = SafeTargetPath(auditEvent.Path),
            authResult = SafeValue(auditEvent.AuthResult),
            status = auditEvent.StatusCode,
            succeeded = auditEvent.Succeeded
        };
        WriteLine("audit", JsonSerializer.Serialize(entry, JsonOptions), settings, _auditGate);
    }

    public ProxyLogPersistenceStatus GetStatus()
    {
        var logDirectory = SafeValue(_dataDirectoryProvider.GetLogsDirectory(), MaxPathLength);
        var settingsResult = _settingsReader.ReadLogPersistenceSettings();
        var settings = settingsResult.Settings;
        DateTimeOffset? lastSuccess;
        ProxyLogPersistenceFailureStatus? lastFailure;
        lock (_statusGate)
        {
            lastSuccess = _lastSuccessfulWriteAtUtc;
            lastFailure = _lastWriteFailure;
        }

        if (settingsResult is ProxyLogPersistenceSettingsReadResult.DisabledDefaultsResult)
        {
            return ProxyLogPersistenceStatus.NoActiveConfiguration(logDirectory, settings);
        }

        return ProxyLogPersistenceStatus.FromSettings(logDirectory, settings, lastSuccess, lastFailure);
    }

    private void WriteLine(string logName, string line, ProxyLogPersistenceSettings settings, object gate)
    {
        try
        {
            lock (gate)
            {
                var logsDirectory = _dataDirectoryProvider.GetLogsDirectory();
                Directory.CreateDirectory(logsDirectory);
                var path = Path.Combine(logsDirectory, $"{logName}.log");
                var lineBytes = LogEncoding.GetByteCount(line) + LogEncoding.GetByteCount(Environment.NewLine);
                RotateIfNeeded(path, settings.MaxFileBytes, settings.MaxFiles, lineBytes);
                File.AppendAllText(path, line + Environment.NewLine, LogEncoding);
                RecordWriteSuccess();
            }
        }
        catch (Exception exception)when (exception is IOException or UnauthorizedAccessException)
        {
            RecordWriteFailure(logName, exception);
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogFailedToPersistLogEntry10002(_logger, logName, exception);
            }
        }
    }

    private void RecordWriteSuccess()
    {
        lock (_statusGate)
        {
            _lastSuccessfulWriteAtUtc = _timeProvider.GetUtcNow();
        }
    }

    private void RecordWriteFailure(string logName, Exception exception)
    {
        var reason = exception is UnauthorizedAccessException ? "access_denied" : "io_error";
        var category = string.Equals(logName, "audit", StringComparison.OrdinalIgnoreCase) ? "admin_audit" : "access";
        var failure = new ProxyLogPersistenceFailureStatus(_timeProvider.GetUtcNow(), category, reason);
        lock (_statusGate)
        {
            _lastWriteFailure = failure;
        }
    }

    private static void RotateIfNeeded(string path, long maxFileBytes, int maxFiles, int nextLineBytes)
    {
        if (!File.Exists(path) || new FileInfo(path).Length + nextLineBytes <= maxFileBytes)
        {
            return;
        }

        if (maxFiles <= 1)
        {
            File.Delete(path);
            return;
        }

        for (var index = maxFiles - 1; index >= 1; index--)
        {
            var destination = RotatedPath(path, index);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            var source = index == 1 ? path : RotatedPath(path, index - 1);
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
    }

    private static string RotatedPath(string path, int index)
    {
        var directory = Path.GetDirectoryName(path)!;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory, $"{fileName}.{index}{extension}");
    }

    private static string? SafeTargetPath(string? value)
    {
        var safe = SafeValue(value, MaxPathLength);
        if (string.IsNullOrWhiteSpace(safe))
        {
            return safe;
        }

        var queryIndex = safe.IndexOfAny(['?', '#']);
        return queryIndex >= 0 ? safe[..queryIndex] : safe;
    }

    private static string? SafeValue(string? value, int maxLength = MaxTextLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var builder = new StringBuilder(Math.Min(trimmed.Length, maxLength));
        foreach (var character in trimmed)
        {
            if (builder.Length >= maxLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
