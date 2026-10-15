using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Worker;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class OrganizationJobWorkerTests
{
    [Theory]
    [InlineData("completed", 2, 0, 2)]
    [InlineData("handler-failed", 1, 1, 2)]
    [InlineData("claim-failed", 1, 0, 1)]
    public async Task ARCH_08_LoggingFailureCannotStopTheNextOrganization(
        string scenario, int completions, int failures, int executions)
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var store = new Store(first, second, scenario == "claim-failed");
        var handler = new Handler(scenario == "handler-failed" ? first : null);
        var observerLog = new LogSink(true, true);
        var loopLog = new LogSink(scenario != "claim-failed", true);
        var processor = new BackgroundJobProcessor(store, new Clock(), [handler],
            new BackgroundJobDiagnostics(new Logger<BackgroundJobDiagnostics>(observerLog)));
        using var worker = new OrganizationJobWorker(processor, new OrganizationJobScope([first, second]),
            new Logger<OrganizationJobWorker>(loopLog));
        try
        {
            await worker.StartAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(worker.ExecuteTask);
            await Task.WhenAny(store.SecondOrganizationDrained.Task, worker.ExecuteTask)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(worker.ExecuteTask.IsFaulted, "Optional logging stopped the actual Worker loop.");
            Assert.True(store.SecondOrganizationDrained.Task.IsCompletedSuccessfully);
            Assert.Equal(completions, store.Completions.Count); Assert.Equal(failures, store.Failures.Count);
            Assert.Equal(executions, handler.Executions.Count);
            Assert.Single(handler.Executions, id => id == second); Assert.Contains(second, store.Completions);
            Assert.Equal(executions, observerLog.InformationCalls);
            Assert.Equal(executions, loopLog.InformationCalls);
            if (scenario == "handler-failed") Assert.Equal("job_handler_failed", Assert.Single(store.Failures).Code);
            if (scenario == "claim-failed") Assert.True(loopLog.WarningCalls > 0);
            else Assert.Equal(0, loopLog.WarningCalls);
            foreach (var text in observerLog.Messages.Concat(loopLog.Messages))
            {
                Assert.DoesNotContain("private-handler-message", text);
                Assert.DoesNotContain("private-queue-message", text);
                Assert.DoesNotContain("private-payload", text);
                Assert.DoesNotContain("private-logger-message", text);
            }
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }
        Assert.NotNull(worker.ExecuteTask); Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Handler(Guid? failOrganization) : IBackgroundJobHandler
    {
        public string JobType => "TEST_JOB";
        public string ServiceIdentity => "test-service";
        public ConcurrentBag<Guid> Executions { get; } = [];
        public Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Executions.Add(job.OrganizationId);
            return job.OrganizationId == failOrganization
                ? Task.FromException(new InvalidOperationException("private-handler-message")) : Task.CompletedTask;
        }
    }
    private sealed class Store(Guid first, Guid second, bool failClaim) : IBackgroundJobStore
    {
        private readonly HashSet<Guid> _claimed = [];
        public ConcurrentBag<Guid> Completions { get; } = [];
        public ConcurrentBag<(Guid Organization, string Code)> Failures { get; } = [];
        public TaskCompletionSource SecondOrganizationDrained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ClaimedBackgroundJob?> ClaimAsync(Guid organizationId, Guid workerId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failClaim && organizationId == first) throw new InvalidOperationException("private-queue-message");
            if (!_claimed.Add(organizationId))
            {
                if (organizationId == second) SecondOrganizationDrained.TrySetResult();
                return Task.FromResult<ClaimedBackgroundJob?>(null);
            }
            return Task.FromResult<ClaimedBackgroundJob?>(new(Guid.NewGuid(), organizationId, "TEST_JOB", Guid.NewGuid(),
                "test-service", "safe-test-reference", "{\"private-payload\":true}", 1, Guid.NewGuid(), workerId, DateTimeOffset.UtcNow.AddMinutes(2)));
        }
        public Task<bool> CompleteAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, CancellationToken cancellationToken = default)
        { Completions.Add(organizationId); return Task.FromResult(true); }
        public Task<bool> FailAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, string errorCode, CancellationToken cancellationToken = default)
        { Failures.Add((organizationId, errorCode)); return Task.FromResult(true); }
    }
    private sealed class LogSink(bool throwInformation, bool throwWarning)
    {
        private int _information, _warning;
        public int InformationCalls => _information;
        public int WarningCalls => _warning;
        public ConcurrentQueue<string> Messages { get; } = [];
        public void Record(LogLevel level, string message)
        {
            Messages.Enqueue(message);
            if (level == LogLevel.Information) Interlocked.Increment(ref _information);
            if (level == LogLevel.Warning) Interlocked.Increment(ref _warning);
            if (level == LogLevel.Information && throwInformation || level == LogLevel.Warning && throwWarning)
                throw new InvalidOperationException("private-logger-message");
        }
    }
    private sealed class Logger<T>(LogSink sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Record(logLevel, formatter(state, exception));
    }
}
