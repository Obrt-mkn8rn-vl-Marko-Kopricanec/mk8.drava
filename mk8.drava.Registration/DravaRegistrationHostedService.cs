using Grpc.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Hosting;
using Mk8.Drava.Contracts.Registration.V1;

namespace Mk8.Drava.Registration;

internal sealed class DravaRegistrationHostedService : BackgroundService, IAsyncDisposable
{
    private readonly DravaRegistrationOptions _options;
    private readonly DravaRegistrationState _state;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Func<IRegistrationSession> _createSession;
    private readonly TimeProvider _clock;
    private readonly Lock _stopGate = new();
    private Task? _stopTask;
    private long _shutdownTimestamp = long.MinValue;

    public DravaRegistrationHostedService(DravaRegistrationOptions options, DravaRegistrationState state, IHostApplicationLifetime lifetime, IServer server)
        : this(options, state, lifetime, () => new RegistrationSession(options, server), TimeProvider.System) { }

    internal DravaRegistrationHostedService(DravaRegistrationOptions options, DravaRegistrationState state, IHostApplicationLifetime lifetime, Func<IRegistrationSession> createSession, TimeProvider? clock = null)
    {
        _options = options;
        _state = state;
        _lifetime = lifetime;
        _createSession = createSession;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var running = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifetime.ApplicationStopping);
        var stopping = _lifetime.ApplicationStopping.Register(() => BeginStopping());
        await using var stoppingLifetime = stopping.ConfigureAwait(false);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await using var registrationLifetime = registration.ConfigureAwait(false);
        try
        {
            await started.Task.WaitAsync(running.Token).ConfigureAwait(false);
            running.Token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _shutdownTimestamp) != long.MinValue) return;
            using var session = _createSession();
            try { await RenewAsync(session, running.Token).ConfigureAwait(false); }
            finally { await DrainAsync(session).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested) { }
        finally { BeginStopping(); }
    }

    private async Task RenewAsync(IRegistrationSession session, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && Volatile.Read(ref _shutdownTimestamp) == long.MinValue)
        {
            var delay = TimeSpan.FromSeconds(_options.PendingRetrySeconds);
            try
            {
                var result = await session.RenewAsync(stoppingToken).ConfigureAwait(false);
                if (result is null)
                {
                    _state.Failed();
                }
                else
                {
                    _state.Accept(result);
                    delay = RenewalDelay(_options, result, session.AgentDescriptor);
                }
            }
            catch (Exception exception) when (exception is RpcException or HttpRequestException or InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                _state.Failed();
            }
            await Task.Delay(delay, _clock, stoppingToken).ConfigureAwait(false);
        }
    }

    internal static TimeSpan RenewalDelay(DravaRegistrationOptions settings, RegistrationStatus status, Mk8.Drava.Configuration.NodeAgentDescriptor? agent)
    {
        var seconds = Math.Min(status.RenewAfterSeconds, (agent?.MappingLeaseSeconds ?? 300) / 3d);
        if (status.Phase != RegistrationPhase.Ready) seconds = Math.Min(seconds, settings.PendingRetrySeconds);
        var jitter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100 - settings.RenewJitterPercent, 101 + settings.RenewJitterPercent) / 100d;
        return TimeSpan.FromSeconds(seconds * jitter);
    }

    private async Task DrainAsync(IRegistrationSession session)
    {
        var began = BeginStopping();
        var remaining = TimeSpan.FromMilliseconds(_options.ShutdownDeadlineMilliseconds) - _clock.GetElapsedTime(began);
        if (remaining <= TimeSpan.Zero) { _state.Failed(); return; }
        using var deadline = new CancellationTokenSource(remaining, _clock);
        try
        {
            var result = await session.DrainAsync(deadline.Token).ConfigureAwait(false);
            if (result is null) _state.Failed();
            else _state.Accept(result);
        }
        catch (Exception exception) when (exception is RpcException or HttpRequestException or InvalidDataException or IOException or System.Text.Json.JsonException or OperationCanceledException)
        {
            _state.Failed();
        }
    }

    private long BeginStopping()
    {
        var now = _clock.GetTimestamp();
        var observed = Interlocked.CompareExchange(ref _shutdownTimestamp, now, long.MinValue);
        _state.BeginStopping();
        return observed == long.MinValue ? now : observed;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        BeginStopping();
        Task completion;
        lock (_stopGate)
        {
            // Publish one stop owner before cancellation can invoke callbacks that reenter the host.
            completion = _stopTask ??= Task.Run(StopOwnedAsync, CancellationToken.None);
        }
        return completion.WaitAsync(cancellationToken);
    }

    private async Task StopOwnedAsync()
    {
        await base.StopAsync(CancellationToken.None).ConfigureAwait(false);
        // BackgroundService suppresses execution failures when stopping; each SDK stop caller joins them.
        if (ExecuteTask is { IsFaulted: true } execution) await execution.ConfigureAwait(false);
    }

    public override void Dispose()
    {
        BeginStopping();
        // ExecuteAsync retains the session until pending operations and drain have settled.
        base.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
        finally { base.Dispose(); GC.SuppressFinalize(this); }
    }
}
