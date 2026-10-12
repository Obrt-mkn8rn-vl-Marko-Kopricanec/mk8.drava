using System.Net;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class RegisteredReplicaChurnProcessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckingAndDrainingReplicasNeverTakeNewRequestsWhileAcceptedStreamSurvivesMembershipChurnAsync(bool bufferedClient)
    {
        var workload = new ChurnWorkload();
        var first = await DevelopmentHttpUpstream.StartAsync(workload.Handler("first")).ConfigureAwait(true);
        await using var firstLifetime = first.ConfigureAwait(true);
        var second = await DevelopmentHttpUpstream.StartAsync(workload.Handler("second")).ConfigureAwait(true);
        await using var secondLifetime = second.ConfigureAwait(true);
        var third = await DevelopmentHttpUpstream.StartAsync(workload.Handler("third")).ConfigureAwait(true);
        await using var thirdLifetime = third.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        // Three successive admissions at the default ten-second probe cadence
        // can consume the entire sixty-second test guard. Preserve hysteresis
        // while using an explicit local cadence for this membership control.
        var lifecycle = new DevelopmentLifecycleSettings
        {
            Registration = new RegistrationSettings { ReadinessIntervalMilliseconds = 1000 },
        };
        var proxy = await TwoProcessProxy.StartAsync(first.Port, enrolledSite: true, manualRoute: false,
            dnsPort: dns.Port, lifecycleSettings: lifecycle).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var registration = new DevelopmentRegistrationClient(proxy);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var firstIdentity = DevelopmentReplicaRegistration.Identity();
        await registration.SubmitAsync(DevelopmentReplicaRegistration.Register(firstIdentity, first.Port), deadline.Token).ConfigureAwait(true);
        await DevelopmentReplicaRegistration.WaitReadyAsync(registration, firstIdentity, deadline.Token).ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        var held = ReadHeldAsync(client.Client, workload, bufferedClient, deadline.Token);
        var failures = new List<Exception>();
        try
        {
            await workload.PublicHead.Task.WaitAsync(deadline.Token).ConfigureAwait(true);
            await VerifyMembershipChurnAsync(registration, client, second.Port, third.Port, firstIdentity, workload, held, deadline.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or Xunit.Sdk.XunitException)
        {
            failures.Add(exception);
        }
        finally
        {
            workload.Release.TrySetResult();
            try { await held.ConfigureAwait(true); }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or Xunit.Sdk.XunitException) { failures.Add(exception); }
            output.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"held_stream bufferedClient={bufferedClient} emittedPulses={workload.StreamPulses} receivedBytes={workload.ReceivedBytes} receivedFinal={workload.ReceivedFinal} originCompleted={workload.OriginCompleted} releaseRequested={workload.Release.Task.IsCompleted}"));
        }
        if (failures.Count != 0) throw new AggregateException("Membership churn and held-stream outcomes", failures);
        Assert.Equal(1, client.ConnectedSockets);
        Assert.Equal(1, workload.HeldRequests);
        Assert.Equal(24, workload.ShortRequests);
    }

    private static async Task VerifyMembershipChurnAsync(DevelopmentRegistrationClient registration, DevelopmentSiteClient client,
        int secondPort, int thirdPort, RegistrationIdentity first, ChurnWorkload workload, Task held, CancellationToken cancellationToken)
    {
        var second = DevelopmentReplicaRegistration.Identity();
        var third = DevelopmentReplicaRegistration.Identity();
        await registration.SubmitAsync(DevelopmentReplicaRegistration.Register(second, secondPort), cancellationToken).ConfigureAwait(true);
        var checking = await registration.SubmitAsync(DevelopmentReplicaRegistration.Register(third, thirdPort), cancellationToken).ConfigureAwait(true);
        Assert.NotEqual(RegistrationPhase.Ready, checking.Phase);
        await workload.ThirdRejected.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
        await DevelopmentReplicaRegistration.WaitReadyAsync(registration, second, cancellationToken).ConfigureAwait(true);
        var rejected = await registration.SubmitAsync(new RegistrationCommand { Identity = third, Operation = RegistrationOperation.Status }, cancellationToken).ConfigureAwait(true);
        Assert.NotEqual(RegistrationPhase.Ready, rejected.Phase);
        await AssertShortsAsync(client.Client, "second", cancellationToken).ConfigureAwait(true);
        Assert.False(held.IsCompleted);

        var drainedFirst = await registration.SubmitAsync(new RegistrationCommand { Identity = first, Operation = RegistrationOperation.Drain }, cancellationToken).ConfigureAwait(true);
        Assert.Equal(RegistrationPhase.Draining, drainedFirst.Phase);
        workload.MakeThirdReady();
        await DevelopmentReplicaRegistration.WaitReadyAsync(registration, third, cancellationToken).ConfigureAwait(true);
        var drainedSecond = await registration.SubmitAsync(new RegistrationCommand { Identity = second, Operation = RegistrationOperation.Drain }, cancellationToken).ConfigureAwait(true);
        Assert.Equal(RegistrationPhase.Draining, drainedSecond.Phase);
        await AssertShortsAsync(client.Client, "third", cancellationToken).ConfigureAwait(true);
        Assert.False(held.IsCompleted);
    }

    private static async Task ReadHeldAsync(HttpClient client, ChurnWorkload workload, bool bufferedClient, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri("/held", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        workload.PublicHead.TrySetResult();
        var body = bufferedClient
            ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true)
            : await ReadIncrementalBodyAsync(response.Content, workload, cancellationToken).ConfigureAwait(true);
        if (bufferedClient) workload.RecordReceived(System.Text.Encoding.ASCII.GetBytes(body));
        Assert.StartsWith("o", body, StringComparison.Ordinal);
        Assert.EndsWith("k", body, StringComparison.Ordinal);
        Assert.Equal(workload.StreamPulses, body.Length - 2);
        Assert.All(body.AsSpan(1, body.Length - 2).ToArray(), static value => Assert.Equal('.', value));
    }

    private static async Task<string> ReadIncrementalBodyAsync(HttpContent content, ChurnWorkload workload, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
        var buffer = new byte[64];
        var total = 0;
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(true)) != 0)
        {
            workload.RecordReceived(buffer.AsSpan(total, count));
            total += count;
            Assert.InRange(total, 1, 63);
        }
        return System.Text.Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static async Task AssertShortsAsync(HttpClient client, string expected, CancellationToken cancellationToken)
    {
        for (var index = 0; index < 12; index++)
        {
            using var response = await client.GetAsync(new Uri("/short", UriKind.Relative), cancellationToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(HttpVersion.Version20, response.Version);
            Assert.Equal(expected, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
        }
    }

    private sealed class ChurnWorkload
    {
        private int _thirdReady;
        private int _heldRequests;
        private int _shortRequests;
        private int _streamPulses;
        private int _receivedBytes;
        private int _receivedFinal;
        private int _originCompleted;
        public int HeldRequests => Volatile.Read(ref _heldRequests);
        public int ShortRequests => Volatile.Read(ref _shortRequests);
        public int StreamPulses => Volatile.Read(ref _streamPulses);
        public int ReceivedBytes => Volatile.Read(ref _receivedBytes);
        public bool ReceivedFinal => Volatile.Read(ref _receivedFinal) != 0;
        public bool OriginCompleted => Volatile.Read(ref _originCompleted) != 0;
        public void RecordReceived(ReadOnlySpan<byte> bytes)
        {
            Interlocked.Add(ref _receivedBytes, bytes.Length);
            if (bytes.Contains((byte)'k')) Volatile.Write(ref _receivedFinal, 1);
        }
        public TaskCompletionSource PublicHead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ThirdRejected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void MakeThirdReady() => Volatile.Write(ref _thirdReady, 1);

        public RequestDelegate Handler(string replica) => async context =>
        {
            if (context.Request.Path == "/ready")
            {
                context.Response.StatusCode = string.Equals(replica, "third", StringComparison.Ordinal) && Volatile.Read(ref _thirdReady) == 0 ? 503 : 200;
                if (context.Response.StatusCode == 503) ThirdRejected.TrySetResult();
                await context.Response.WriteAsync("ready", context.RequestAborted).ConfigureAwait(false);
            }
            else if (context.Request.Path == "/held")
            {
                Assert.Equal("first", replica, StringComparer.Ordinal);
                Assert.Equal(1, Interlocked.Increment(ref _heldRequests));
                await context.Response.WriteAsync("o", context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                await KeepStreamProgressingAsync(context).ConfigureAwait(false);
                await context.Response.WriteAsync("k", context.RequestAborted).ConfigureAwait(false);
                Volatile.Write(ref _originCompleted, 1);
            }
            else
            {
                Interlocked.Increment(ref _shortRequests);
                await context.Response.StartAsync(context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsync(replica, context.RequestAborted).ConfigureAwait(false);
            }
        };

        private async Task KeepStreamProgressingAsync(HttpContext context)
        {
            // Real readiness hysteresis spans multiple probe intervals. Keep this
            // accepted stream active within the unchanged response idle budget.
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!Release.Task.IsCompleted && await timer.WaitForNextTickAsync(context.RequestAborted).ConfigureAwait(false))
            {
                if (Release.Task.IsCompleted) break;
                await context.Response.WriteAsync(".", context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                Interlocked.Increment(ref _streamPulses);
            }
        }
    }
}
