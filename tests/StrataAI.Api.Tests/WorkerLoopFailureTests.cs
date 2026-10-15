using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Worker;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class WorkerLoopFailureTests
{
    [Theory]
    [InlineData("organization", "claim", false)]
    [InlineData("organization", "claim", true)]
    [InlineData("organization", "finish", false)]
    [InlineData("organization", "finish", true)]
    [InlineData("organization", "unexpected-cancellation", false)]
    [InlineData("organization", "unexpected-cancellation", true)]
    [InlineData("identity_email", "claim", false)]
    [InlineData("identity_email", "claim", true)]
    [InlineData("identity_email", "finish", false)]
    [InlineData("identity_email", "finish", true)]
    [InlineData("identity_email", "unexpected-cancellation", false)]
    [InlineData("identity_email", "unexpected-cancellation", true)]
    public Task ARCH_08_Escaping_queue_failure_is_counted_once_and_next_pass_survives(
        string queue, string failure, bool listenerFailure) => Run(queue, failure, listenerFailure);

    [Theory]
    [InlineData("organization", "empty")]
    [InlineData("organization", "shutdown")]
    [InlineData("identity_email", "empty")]
    [InlineData("identity_email", "shutdown")]
    [InlineData("organization", "handled")]
    [InlineData("identity_email", "handled")]
    public Task ARCH_08_Empty_shutdown_and_decided_retry_are_not_loop_failures(string queue, string scenario) =>
        Run(queue, scenario, false);

    private static async Task Run(string queue, string scenario, bool listenerFailure)
    {
        using var services = WorkerJobTelemetryTests.Services();
        var meter = services.GetRequiredService<IMeterFactory>().Create(WorkerJobTelemetry.MeterName);
        var samples = new ConcurrentQueue<(string Name, long Count, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, observer) =>
        {
            if (ReferenceEquals(instrument.Meter, meter)) observer.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, count, tags, _) =>
        {
            samples.Enqueue((instrument.Name, count, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
            if (listenerFailure) throw new InvalidOperationException("private-listener-message");
        });
        listener.Start();
        var fixture = new Store(scenario);
        var sink = new Sink();
        var telemetry = services.GetRequiredService<WorkerJobTelemetry>();
        using BackgroundService worker = queue == "organization"
            ? ActivatorUtilities.CreateInstance<OrganizationJobWorker>(services,
                new BackgroundJobProcessor(fixture, new Clock(), [fixture],
                    new BackgroundJobDiagnostics(new Logger<BackgroundJobDiagnostics>(sink), telemetry)),
                new OrganizationJobScope([fixture.OrganizationId]), new Logger<OrganizationJobWorker>(sink))
            : ActivatorUtilities.CreateInstance<IdentityEmailWorker>(services,
                new IdentityDeliveryProcessor(fixture, new Signer(), fixture,
                    new("sender@example.test", "https://app.example.test", "fixture"), new Clock(),
                    new IdentityDeliveryDiagnostics(new Logger<IdentityDeliveryDiagnostics>(sink), telemetry)),
                new Logger<IdentityEmailWorker>(sink));
        try
        {
            await worker.StartAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(worker.ExecuteTask);
            await Task.WhenAny(fixture.Progress.Task, worker.ExecuteTask)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(fixture.Progress.Task.IsCompletedSuccessfully, "The real Worker did not reach its next pass or cancellable wait.");
            Assert.False(worker.ExecuteTask.IsFaulted);
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }
        Assert.NotNull(worker.ExecuteTask); Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        var escaping = scenario is "claim" or "finish" or "unexpected-cancellation";
        if (escaping)
        {
            var sample = Assert.Single(samples);
            Assert.Equal("strataai.worker.loop.failures", sample.Name);
            Assert.Equal(1, sample.Count); Assert.Single(sample.Tags); Assert.Equal(queue, sample.Tags["queue"]);
            Assert.Equal(1, sink.Warnings);
        }
        else if (scenario == "handled")
        {
            var sample = Assert.Single(samples);
            Assert.Equal("strataai.worker.jobs", sample.Name);
            Assert.Equal(1, sample.Count); Assert.Equal(2, sample.Tags.Count);
            Assert.Equal(queue, sample.Tags["queue"]); Assert.Equal("retried", sample.Tags["outcome"]);
            Assert.Equal(0, sink.Warnings);
        }
        else Assert.Empty(samples);
        Assert.Equal(scenario is "empty" or "shutdown" ? 1 : 2, fixture.Claims);
        Assert.Equal(scenario is "finish" or "handled" ? 1 : 0, fixture.Executions);
        Assert.Equal(scenario == "finish" ? 1 : 0, fixture.Finishes);
        Assert.Equal(scenario == "handled" ? 1 : 0, fixture.Retries);
        Assert.All(sink.Messages, text =>
        {
            foreach (var secret in new[] { "private-queue-message", "private-provider-message", "private-token", "private-recipient@example.test" })
                Assert.DoesNotContain(secret, text);
        });
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Signer : IIdentityDeliveryTokenSigner
    {
        public string CurrentKeyId => "fixture";
        public string Derive(Guid id, IdentityTokenPurpose purpose, string keyId) => "private-token";
    }
    private sealed class Store(string scenario) : IBackgroundJobStore, IBackgroundJobHandler, IIdentityDeliveryStore, IIdentityEmailProvider
    {
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public string JobType => "TEST_JOB";
        public string ServiceIdentity => "test-service";
        public int Claims, Executions, Finishes, Retries;
        public TaskCompletionSource Progress { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task<bool> Claim(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref Claims) > 1 || scenario == "empty") { Progress.TrySetResult(); return false; }
            if (scenario == "shutdown") { Progress.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            if (scenario == "claim") throw new InvalidOperationException("private-queue-message");
            if (scenario == "unexpected-cancellation") throw new OperationCanceledException("private-queue-message");
            return true;
        }
        public async Task<ClaimedBackgroundJob?> ClaimAsync(Guid organizationId, Guid workerId, CancellationToken cancellationToken = default) =>
            await Claim(cancellationToken) ? new(Guid.NewGuid(), organizationId, JobType, Guid.NewGuid(), ServiceIdentity,
                "safe-reference", "{}", 1, Guid.NewGuid(), workerId, DateTimeOffset.UtcNow.AddMinutes(2)) : null;
        public async Task<IdentityDeliveryJob?> ClaimAsync(Guid workerId, CancellationToken cancellationToken) =>
            await Claim(cancellationToken) ? new(Guid.NewGuid(), Guid.NewGuid(), IdentityTokenPurpose.VerifyEmail, "fixture", "safe-reference",
                "private-recipient@example.test", "sender@example.test", "https://app.example.test", "fixture", 1,
                Guid.NewGuid(), workerId, DateTimeOffset.UtcNow.AddMinutes(2), DateTimeOffset.UtcNow.AddMinutes(5)) : null;
        public Task<string?> GetUsableTokenHashAsync(IdentityDeliveryJob job, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("private-token"))));
        private Task Execute()
        {
            Interlocked.Increment(ref Executions);
            return scenario == "handled" ? Task.FromException(new InvalidOperationException("private-provider-message")) : Task.CompletedTask;
        }
        public Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken) => Execute();
        public async Task<Guid> SendAsync(IdentityEmailMessage message, CancellationToken cancellationToken) { await Execute(); return Guid.NewGuid(); }
        public Task<bool> CompleteAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Finishes); throw new InvalidOperationException("private-queue-message"); }
        public Task<bool> FailAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, string errorCode, CancellationToken cancellationToken = default)
        { Assert.Equal("job_handler_failed", errorCode); Interlocked.Increment(ref Retries); return Task.FromResult(true); }
        public Task<bool> FinishAsync(IdentityDeliveryJob job, IdentityDeliveryOutcome outcome, string? errorCode, Guid? receiptId, CancellationToken cancellationToken)
        {
            if (outcome == IdentityDeliveryOutcome.Retry)
            { Assert.Equal("identity_provider_unavailable", errorCode); Assert.Null(receiptId); Interlocked.Increment(ref Retries); return Task.FromResult(true); }
            Assert.Equal(IdentityDeliveryOutcome.Sent, outcome); Assert.Null(errorCode); Assert.NotNull(receiptId);
            Interlocked.Increment(ref Finishes); throw new InvalidOperationException("private-queue-message");
        }
    }
    private sealed class Sink
    {
        public int Warnings;
        public ConcurrentQueue<string> Messages { get; } = [];
    }
    private sealed class Logger<T>(Sink sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            sink.Messages.Enqueue(formatter(state, exception));
            if (level == LogLevel.Warning) Interlocked.Increment(ref sink.Warnings);
            throw new InvalidOperationException("private-logger-message");
        }
    }
}
