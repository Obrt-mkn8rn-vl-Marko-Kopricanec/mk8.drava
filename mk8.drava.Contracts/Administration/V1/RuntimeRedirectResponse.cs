namespace Mk8.Drava.Contracts.Administration.V1;
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "Versioned wire contract retains raw redirect text for Application validation.")]
public sealed record RuntimeRedirectResponse(int StatusCode, [property: System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "Versioned wire contract retains raw redirect text for Application validation.")] string TargetUrl, string TargetPath, bool PreserveQuery)
{
}
