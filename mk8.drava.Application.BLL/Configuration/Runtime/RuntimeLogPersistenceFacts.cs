namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeLogPersistenceFacts
{
    private const long MinimumLogFileBytes = 4 * 1024;
    private const long MaximumLogFileBytes = 1024L * 1024 * 1024;
    private const int MinimumLogFileCount = 1;
    private const int MaximumLogFileCount = 128;
    public static void Validate(long maxFileBytes, int maxFiles)
    {
        if (maxFileBytes is < MinimumLogFileBytes or > MaximumLogFileBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        }

        if (maxFiles is < MinimumLogFileCount or > MaximumLogFileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles));
        }
    }
}
