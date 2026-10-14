using System.Text.Json;
using Supprocom.Secrets;

namespace Mk8.Drava.Configuration;

// Parser messages and inner exception graphs can contain private input. Keep public refusal types and provider codes.
internal static class ProtectedConfigurationOperation
{
    internal static async Task<TResult> RunAsync<TResult>(Func<Task<TResult>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (SupprocomSecretsException exception)
        {
#pragma warning disable MA0054 // Retaining the original inner graph exposes private input; preserve only the refusal type/code.
            throw new SupprocomSecretsException(exception.Code, "Protected configuration provider refused input.");
#pragma warning restore MA0054
        }
        catch (JsonException)
        {
#pragma warning disable MA0054 // Parser diagnostics include private property names; the real-host canary tests require removing the original graph.
            throw new JsonException("Protected configuration input is invalid.");
#pragma warning restore MA0054
        }
    }

    internal static async Task RunAsync(Func<Task> operation)
    {
        try { await operation().ConfigureAwait(false); }
        catch (SupprocomSecretsException exception)
        {
#pragma warning disable MA0054 // Retaining the original inner graph exposes private input; preserve only the refusal type/code.
            throw new SupprocomSecretsException(exception.Code, "Protected configuration provider refused input.");
#pragma warning restore MA0054
        }
        catch (JsonException)
        {
#pragma warning disable MA0054 // Parser diagnostics include private property names; the real-host canary tests require removing the original graph.
            throw new JsonException("Protected configuration input is invalid.");
#pragma warning restore MA0054
        }
    }
}
