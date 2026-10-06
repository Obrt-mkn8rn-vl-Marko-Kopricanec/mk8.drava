using Mk8.Drava.Application.BLL.ControlPlane.Acme;

namespace Mk8.Drava.Application.INF.Acme;
public sealed class DisabledAcmeCertificateIssuer : IAcmeCertificateIssuer
{
    public ValueTask<AcmeCertificateIssueResult> IssueAsync(AcmeCertificateIssueRequest request, AcmeChallengeStore challengeStore, CancellationToken cancellationToken)
    {
        _ = request;
        _ = challengeStore;
        _ = cancellationToken;
        return ValueTask.FromResult(AcmeCertificateIssueResult.Failed("No live ACME issuer is configured in this build; the lifecycle manager is ready for an ACME issuer adapter."));
    }
}
