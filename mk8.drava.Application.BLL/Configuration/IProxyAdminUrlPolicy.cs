namespace Mk8.Drava.Application.BLL.Configuration;
public interface IProxyAdminUrlPolicy
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "This validator must classify raw potentially malformed text using existing HTTP/HTTPS and loopback rules; accepting only Uri would bypass syntax rejection or change the established false/exception contract.")]
    bool IsValid(string url);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "This validator must classify raw potentially malformed text using existing HTTP/HTTPS and loopback rules; accepting only Uri would bypass syntax rejection or change the established false/exception contract.")]
    bool IsLocal(string url);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "This validator must classify raw potentially malformed text using existing HTTP/HTTPS and loopback rules; accepting only Uri would bypass syntax rejection or change the established false/exception contract.")]
    bool IsNonLocal(string url);
}
