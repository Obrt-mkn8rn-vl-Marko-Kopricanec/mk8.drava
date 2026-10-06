namespace Mk8.Drava.Application.BLL.Configuration;
public abstract record ProxySafeRelativePathResult
{
    private ProxySafeRelativePathResult()
    {
    }

    public static ProxySafeRelativePathResult Unsafe { get; } = new UnsafeResult();

    public static ProxySafeRelativePathResult Safe(string relativePath)
    {
        return new SafeResult(relativePath);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record SafeResult : ProxySafeRelativePathResult
    {
        public SafeResult(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("Safe relative path is required.", nameof(relativePath));
            }

            RelativePath = relativePath;
        }

        public string RelativePath { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record UnsafeResult : ProxySafeRelativePathResult;
}
