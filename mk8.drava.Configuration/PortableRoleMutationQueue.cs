using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace Mk8.Drava.Configuration;

// One owner settles every admitted operation before its close completes.
internal sealed class PortableRoleMutationQueue
{
    private readonly Channel<Mutation> _queue = Channel.CreateBounded<Mutation>(new BoundedChannelOptions(8)
    {
        SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false,
    });
    private readonly Lock _gate = new();
    private readonly Task _drain;
    private bool _closing;
    private ExceptionDispatchInfo? _failure;
    private ExceptionDispatchInfo? _cancellation;

    internal PortableRoleMutationQueue() => _drain = ProcessAsync();

    internal Task<TResult> EnqueueAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutation = new Mutation<TResult>(operation, completion, cancellationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _failure?.Throw();
            if (!_queue.Writer.TryWrite(mutation)) throw new InvalidOperationException("Protected role mutation admission exceeds its bound.");
        }
        return completion.Task;
    }

    internal ValueTask CloseAsync()
    {
        lock (_gate)
        {
            _closing = true;
            _queue.Writer.TryComplete();
            return new ValueTask(_drain);
        }
    }

    private async Task ProcessAsync()
    {
        await foreach (var mutation in _queue.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            try
            {
                _failure?.Throw();
                mutation.CancellationToken.ThrowIfCancellationRequested();
                await mutation.ExecuteAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                _cancellation ??= ExceptionDispatchInfo.Capture(exception);
                mutation.Fail(exception);
            }
            catch (Exception exception)
            {
                lock (_gate) { _failure ??= ExceptionDispatchInfo.Capture(exception); }
                mutation.Fail(exception);
                while (_queue.Reader.TryRead(out var pending)) pending.Fail(exception);
                _queue.Writer.TryComplete();
                throw;
            }
        }
        _cancellation?.Throw();
    }

    private abstract class Mutation(CancellationToken cancellationToken)
    {
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal abstract Task ExecuteAsync();
        internal abstract void Fail(Exception exception);
    }

    private sealed class Mutation<TResult>(Func<CancellationToken, Task<TResult>> operation, TaskCompletionSource<TResult> completion, CancellationToken cancellationToken) : Mutation(cancellationToken)
    {
        internal override async Task ExecuteAsync()
        {
            var result = await operation(CancellationToken).ConfigureAwait(false);
            completion.TrySetResult(result);
        }

        internal override void Fail(Exception exception)
        {
            if (exception is OperationCanceledException canceled) completion.TrySetCanceled(canceled.CancellationToken);
            else completion.TrySetException(exception);
        }
    }
}
