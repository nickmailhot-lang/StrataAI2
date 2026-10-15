using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Runtime;
using StrataAI.Worker;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class WorkerLoggingTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ARCH_08_SentIdentityMailAndNextPassSurviveThrowingDiagnostics(bool metricFailure)
    {
        using var services = WorkerJobTelemetryTests.Services();
        using var metrics = WorkerJobTelemetryTests.Capture.For(services, metricFailure);
        var store = new IdentityStore(); var provider = new EmailProvider(); var log = new Sink { ThrowInformation = true, ThrowWarning = true };
        var processor = new IdentityDeliveryProcessor(store, new Signer(), provider,
            new("sender@example.test", "https://app.example.test", "fixture"), new Clock(),
            new IdentityDeliveryDiagnostics(new Logger<IdentityDeliveryDiagnostics>(log),
                services.GetRequiredService<WorkerJobTelemetry>()));
        using var worker = new IdentityEmailWorker(processor, new Logger<IdentityEmailWorker>(log));
        await RunUntilAsync(worker, store.NextPass.Task);
        Assert.Equal(1, provider.Sends); Assert.Equal(1, store.Finishes); Assert.Equal(IdentityDeliveryOutcome.Sent, store.Outcome);
        metrics.AssertSingle("identity_email", "sent");
        Assert.Equal(1, log.InformationCalls); Assert.Equal(0, log.WarningCalls);
        Assert.NotNull(provider.Message); Assert.Contains("#token=private-token", provider.Message.Text);
        AssertPrivateMessagesAbsent(log);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARCH_08_IssuerAuthorityRetryPassSurvivesThrowingWarning(bool claimFailure)
    {
        var store = new IssuerStore(claimFailure); var log = new Sink { ThrowWarning = true };
        using var worker = new InvitationIssuerAuthorityWorker(store, new Logger<InvitationIssuerAuthorityWorker>(log));
        await RunUntilAsync(worker, store.NextPass.Task);
        Assert.Equal(claimFailure ? 0 : 1, store.Deliveries); Assert.Equal(1, log.WarningCalls);
        if (!claimFailure) Assert.Same(store.Claim, store.Delivered);
        AssertPrivateMessagesAbsent(log);
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("message")]
    [InlineData("dispose")]
    public async Task ARCH_08_HeartbeatStartupAndShutdownSurviveThrowingLogger(string failure)
    {
        var log = new Sink { ThrowBegin = failure == "begin", ThrowInformation = failure == "message", ThrowDispose = failure == "dispose" };
        using var worker = new WorkerHeartbeat(new Logger<WorkerHeartbeat>(log), new Clock(),
            new RuntimeDescriptor(RuntimeMode.Production, new string('a', 40), "1.0.0"));
        await RunUntilAsync(worker, log.InformationStarted.Task);
        Assert.Equal(1, log.InformationCalls); Assert.Equal(failure == "begin" ? 0 : 1, log.Disposals);
        AssertPrivateMessagesAbsent(log);
    }

    private static async Task RunUntilAsync(BackgroundService worker, Task progress)
    {
        try
        {
            await worker.StartAsync(TestContext.Current.CancellationToken); Assert.NotNull(worker.ExecuteTask);
            await Task.WhenAny(progress, worker.ExecuteTask).WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            Assert.True(progress.IsCompletedSuccessfully, "Optional logging prevented the next real service stage.");
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }
        Assert.NotNull(worker.ExecuteTask); Assert.True(worker.ExecuteTask.IsCompleted); Assert.False(worker.ExecuteTask.IsFaulted);
    }

    private static void AssertPrivateMessagesAbsent(Sink log)
    {
        foreach (var message in log.Messages) foreach (var secret in new[] { "private-recipient@example.test", "private-token", "private-provider-message", "private-queue-message", "private-logger-message" })
            Assert.DoesNotContain(secret, message);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Signer : IIdentityDeliveryTokenSigner
    {
        public string CurrentKeyId => "fixture";
        public string Derive(Guid tokenId, IdentityTokenPurpose purpose, string keyId) => "private-token";
    }
    private sealed class EmailProvider : IIdentityEmailProvider
    {
        public int Sends { get; private set; }
        public IdentityEmailMessage? Message { get; private set; }
        public Task<Guid> SendAsync(IdentityEmailMessage message, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Sends++; Message = message; return Task.FromResult(Guid.NewGuid()); }
    }
    private sealed class IdentityStore : IIdentityDeliveryStore
    {
        private int _claims;
        public int Finishes { get; private set; }
        public IdentityDeliveryOutcome? Outcome { get; private set; }
        public TaskCompletionSource NextPass { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IdentityDeliveryJob?> ClaimAsync(Guid workerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _claims) > 1) { NextPass.TrySetResult(); return Task.FromResult<IdentityDeliveryJob?>(null); }
            return Task.FromResult<IdentityDeliveryJob?>(new(Guid.NewGuid(), Guid.NewGuid(), IdentityTokenPurpose.VerifyEmail, "fixture", "safe-reference",
                "private-recipient@example.test", "sender@example.test", "https://app.example.test", "fixture", 1,
                Guid.NewGuid(), workerId, DateTimeOffset.UtcNow.AddMinutes(2), DateTimeOffset.UtcNow.AddMinutes(5)));
        }
        public Task<string?> GetUsableTokenHashAsync(IdentityDeliveryJob job, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("private-token"))));
        public Task<bool> FinishAsync(IdentityDeliveryJob job, IdentityDeliveryOutcome outcome, string? errorCode, Guid? receiptId, CancellationToken cancellationToken)
        { Assert.Null(errorCode); Assert.NotNull(receiptId); Finishes++; Outcome = outcome; return Task.FromResult(true); }
    }
    private sealed class IssuerStore(bool failClaim) : IInvitationIssuerAuthorityDeliveryStore
    {
        private int _claims;
        public int Deliveries { get; private set; }
        public InvitationIssuerAuthorityClaim? Claim { get; private set; }
        public InvitationIssuerAuthorityClaim? Delivered { get; private set; }
        public TaskCompletionSource NextPass { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<InvitationIssuerAuthorityClaim?> ClaimAsync(Guid workerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _claims) > 1) { NextPass.TrySetResult(); return Task.FromResult<InvitationIssuerAuthorityClaim?>(null); }
            if (failClaim) throw new InvalidOperationException("private-queue-message");
            Claim = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), workerId, Guid.NewGuid(), "safe-reference");
            return Task.FromResult<InvitationIssuerAuthorityClaim?>(Claim);
        }
        public Task<bool> DeliverAsync(InvitationIssuerAuthorityClaim claim, int limit, CancellationToken cancellationToken)
        { Assert.Equal(100, limit); Delivered = claim; Deliveries++; return Task.FromResult(false); }
    }
    private sealed class Sink
    {
        public bool ThrowInformation { get; init; }
        public bool ThrowWarning { get; init; }
        public bool ThrowBegin { get; init; }
        public bool ThrowDispose { get; init; }
        public int InformationCalls, WarningCalls, Disposals;
        public ConcurrentQueue<string> Messages { get; } = [];
        public TaskCompletionSource InformationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable Begin()
        { if (ThrowBegin) throw new InvalidOperationException("private-logger-message"); return new Scope(this); }
        public void Record(LogLevel level, string message)
        {
            Messages.Enqueue(message);
            if (level == LogLevel.Information) { Interlocked.Increment(ref InformationCalls); InformationStarted.TrySetResult(); }
            if (level == LogLevel.Warning) Interlocked.Increment(ref WarningCalls);
            if (level == LogLevel.Information && ThrowInformation || level == LogLevel.Warning && ThrowWarning)
                throw new InvalidOperationException("private-logger-message");
        }
        private sealed class Scope(Sink sink) : IDisposable
        {
            public void Dispose()
            { Interlocked.Increment(ref sink.Disposals); if (sink.ThrowDispose) throw new InvalidOperationException("private-logger-message"); }
        }
    }
    private sealed class Logger<T>(Sink sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => sink.Begin();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => sink.Record(logLevel, formatter(state, exception));
    }
}
