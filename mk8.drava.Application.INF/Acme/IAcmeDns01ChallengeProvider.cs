namespace Mk8.Drava.Application.INF.Acme;

internal interface IAcmeDns01ChallengeProvider
{
    ValueTask RecoverAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    ValueTask<AcmeDns01Record> PublishAsync(string host, string value, string operationId, CancellationToken cancellationToken);
    ValueTask<bool> IsPropagatedAsync(AcmeDns01Record record, CancellationToken cancellationToken);
    ValueTask RemoveAsync(AcmeDns01Record record, CancellationToken cancellationToken);
}
