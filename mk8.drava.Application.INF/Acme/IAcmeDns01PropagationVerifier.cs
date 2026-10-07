namespace Mk8.Drava.Application.INF.Acme;

internal interface IAcmeDns01PropagationVerifier
{
    ValueTask<bool> VerifyAsync(string host, string value, CancellationToken cancellationToken);
}
