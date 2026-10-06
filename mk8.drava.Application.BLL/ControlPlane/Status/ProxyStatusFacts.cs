using System.Collections.ObjectModel;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
internal static class ProxyStatusFacts
{
    public static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Values cannot be empty.", parameterName);
        }
    }

    public static void RequireOptionalText(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Values cannot be empty.", parameterName);
        }
    }

    public static void RequireNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Values cannot be negative.");
        }
    }

    public static void RequireOptionalNonNegative(int? value, string parameterName)
    {
        if (value is not null)
        {
            RequireNonNegative(value.Value, parameterName);
        }
    }

    public static void RequireNonNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Values cannot be negative.");
        }
    }

    public static void RequireShutdownWindow(bool isShuttingDown, DateTimeOffset? startedAtUtc, string startedAtParameterName, DateTimeOffset? deadlineUtc, string deadlineParameterName)
    {
        if (!isShuttingDown)
        {
            if (startedAtUtc is not null)
            {
                throw new ArgumentException("Shutdown start cannot be set when shutdown is not active.", startedAtParameterName);
            }

            if (deadlineUtc is not null)
            {
                throw new ArgumentException("Shutdown deadline cannot be set when shutdown is not active.", deadlineParameterName);
            }

            return;
        }

        if (startedAtUtc is null)
        {
            throw new ArgumentException("Shutdown start is required when shutdown is active.", startedAtParameterName);
        }

        if (deadlineUtc is null)
        {
            throw new ArgumentException("Shutdown deadline is required when shutdown is active.", deadlineParameterName);
        }

        if (deadlineUtc.Value < startedAtUtc.Value)
        {
            throw new ArgumentException("Shutdown deadline cannot be before shutdown start.", deadlineParameterName);
        }
    }
}
