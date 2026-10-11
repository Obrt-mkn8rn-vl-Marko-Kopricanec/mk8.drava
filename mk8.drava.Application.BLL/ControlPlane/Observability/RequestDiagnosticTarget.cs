namespace Mk8.Drava.Application.BLL.ControlPlane.Observability;

// Diagnostic projection only: never decode, normalize or replace a wire target.
public static class RequestDiagnosticTarget
{
    public static string? PathOnly(string? target)
    {
        if (target is null) return null;
        var query = target.IndexOfAny(['?', '#']);
        return query >= 0 ? target[..query] : target;
    }
}
