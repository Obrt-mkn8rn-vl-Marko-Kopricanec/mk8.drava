namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IAcmeCertificateIssuer
{
    ValueTask<AcmeCertificateIssueResult> IssueAsync(AcmeCertificateIssueRequest request, AcmeChallengeStore challengeStore, CancellationToken cancellationToken);
}
