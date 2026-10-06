using System.Text.Json;
using Mk8.Drava.Application.DAL.NoConf;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class NoConfPolicyFileTests
{
    [Theory]
    [InlineData("{\"global\":null}")]
    [InlineData("{\"site\":null}")]
    [InlineData("{\"services\":null}")]
    public async Task NullStructuralMembersAreRejectedBeforeReconciliationAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "drava_policy_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json, CancellationToken.None).ConfigureAwait(true);
            await Assert.ThrowsAsync<JsonException>(() => NoConfPolicyFile.ReadAsync(path, CancellationToken.None).AsTask()).ConfigureAwait(true);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"services\":[null]}")]
    [InlineData("{\"site\":{},\"site\":{}}")]
    public async Task NullCollectionEntriesAndDuplicatePropertiesCannotReachTheCompilerAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "drava_policy_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json, CancellationToken.None).ConfigureAwait(true);
            await Assert.ThrowsAsync<InvalidDataException>(() => NoConfPolicyFile.ReadAsync(path, CancellationToken.None).AsTask()).ConfigureAwait(true);
        }
        finally { File.Delete(path); }
    }
}
