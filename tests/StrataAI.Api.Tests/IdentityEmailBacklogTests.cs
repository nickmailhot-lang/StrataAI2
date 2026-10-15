using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;
using StrataAI.Worker;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class IdentityEmailBacklogTests
{
    [Fact]
    public async Task ARCH_08_Identity_counts_are_unavailable_until_read_and_export_never_reads_the_store()
    {
        using var services = Services(); var store = new Reader(_ => Task.FromResult(new IdentityDeliveryBacklog(3, 2)));
        using var worker = new IdentityEmailBacklogWorker(store, services.GetRequiredService<IMeterFactory>());
        using var capture = new Capture(services);
        capture.Read(); capture.AssertUnavailable();
        await worker.SampleAsync(TestContext.Current.CancellationToken);
        capture.Read(); capture.AssertCounts(3, 2);
        capture.Read(); capture.AssertCounts(3, 2); Assert.Equal(1, store.Calls);
    }

    [Theory]
    [InlineData("exception")]
    [InlineData("cancellation")]
    [InlineData("negative-pending")]
    [InlineData("negative-running")]
    public async Task ARCH_08_Identity_failed_observation_discards_previous_counts_and_can_recover(string failure)
    {
        using var services = Services(); var fail = false;
        var store = new Reader(_ => !fail ? Task.FromResult(new IdentityDeliveryBacklog(3, 2))
            : failure == "negative-pending" ? Task.FromResult(new IdentityDeliveryBacklog(-1, 0))
            : failure == "negative-running" ? Task.FromResult(new IdentityDeliveryBacklog(0, -1))
            : Task.FromException<IdentityDeliveryBacklog>(failure == "cancellation"
                ? new OperationCanceledException("private-provider") : new InvalidOperationException("private-provider")));
        using var worker = new IdentityEmailBacklogWorker(store, services.GetRequiredService<IMeterFactory>());
        using var capture = new Capture(services);
        await worker.SampleAsync(TestContext.Current.CancellationToken); capture.Read(); capture.AssertCounts(3, 2);
        fail = true;
        await worker.SampleAsync(TestContext.Current.CancellationToken); capture.Read(); capture.AssertUnavailable();
        fail = false;
        await worker.SampleAsync(TestContext.Current.CancellationToken); capture.Read(); capture.AssertCounts(3, 2);
    }

    [Fact]
    public async Task ARCH_08_Identity_empty_queue_publishes_genuine_zero_counts()
    {
        using var services = Services(); var store = new Reader(_ => Task.FromResult(new IdentityDeliveryBacklog(0, 0)));
        using var worker = new IdentityEmailBacklogWorker(store, services.GetRequiredService<IMeterFactory>());
        using var capture = new Capture(services);
        await worker.SampleAsync(TestContext.Current.CancellationToken); capture.Read(); capture.AssertCounts(0, 0);
    }

    [Fact]
    public async Task ARCH_08_Identity_observation_deadline_is_bounded_and_does_not_report_zero()
    {
        using var services = Services();
        var store = new Reader(async ct => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new(9, 9); });
        using var worker = new IdentityEmailBacklogWorker(store, services.GetRequiredService<IMeterFactory>());
        using var capture = new Capture(services); var clock = Stopwatch.StartNew();
        await worker.SampleAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(7));
        capture.Read(); capture.AssertUnavailable(); Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task ARCH_08_Identity_shutdown_cancels_inflight_read_and_stopped_sample_never_reads()
    {
        using var services = Services(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Reader(async ct => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new(0, 0); });
        using var worker = new IdentityEmailBacklogWorker(store, services.GetRequiredService<IMeterFactory>());
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); }
        finally { await worker.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); }
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancelled.CancelAsync(); await worker.SampleAsync(cancelled.Token); Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task ARCH_08_Identity_instrument_creation_failure_does_not_prevent_observation()
    {
        using var factory = new BrokenFactory(); var store = new Reader(_ => Task.FromResult(new IdentityDeliveryBacklog(1, 0)));
        using var worker = new IdentityEmailBacklogWorker(store, factory);
        await worker.SampleAsync(TestContext.Current.CancellationToken); Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task ARCH_08_Identity_backlog_database_handshake_has_independent_two_second_budget()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        await using var reader = new PostgresIdentityDeliveryBacklogReader(
            $"Host=127.0.0.1;Port={((IPEndPoint)listener.LocalEndpoint).Port};Database=fixture;Username=fixture;Password=fixture;SSL Mode=Disable;Timeout=30;Command Timeout=30");
        var clock = Stopwatch.StartNew(); var sample = reader.ReadAsync(TestContext.Current.CancellationToken);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => sample.WaitAsync(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken));
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ARCH_08_Identity_backlog_precancellation_does_not_open_a_connection()
    {
        await using var reader = new PostgresIdentityDeliveryBacklogReader("Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=fixture;SSL Mode=Disable");
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cancelled.Token));
    }

    private static ServiceProvider Services() => new ServiceCollection().AddMetrics().BuildServiceProvider();
    internal sealed class Reader(Func<CancellationToken, Task<IdentityDeliveryBacklog>> read) : IIdentityDeliveryBacklogReader
    {
        public int Calls;
        public Task<IdentityDeliveryBacklog> ReadAsync(CancellationToken ct) { Interlocked.Increment(ref Calls); return read(ct); }
    }
    private sealed class BrokenFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => throw new InvalidOperationException("private-instrument");
        public void Dispose() { }
    }
    private sealed class Capture : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<(string Name, long Value, Dictionary<string, object?> Tags)> samples = [];
        public Capture(IServiceProvider services)
        {
            var meter = services.GetRequiredService<IMeterFactory>().Create(WorkerJobTelemetry.MeterName);
            listener.InstrumentPublished = (instrument, observer) =>
            {
                if (ReferenceEquals(instrument.Meter, meter) && instrument.Name.StartsWith("strataai.worker.identity.backlog", StringComparison.Ordinal))
                    observer.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => samples.Add((instrument.Name, value,
                tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
            listener.Start();
        }
        public void Read() { samples.Clear(); listener.RecordObservableInstruments(); }
        public void AssertUnavailable()
        {
            var only = Assert.Single(samples); Assert.Equal("strataai.worker.identity.backlog.available", only.Name);
            Assert.Equal(0, only.Value); Assert.Single(only.Tags); Assert.Equal("identity_email", only.Tags["queue"]);
        }
        public void AssertCounts(long pending, long running)
        {
            Assert.Equal(3, samples.Count);
            var available = Assert.Single(samples, s => s.Name.EndsWith(".available", StringComparison.Ordinal));
            Assert.Equal(1, available.Value); Assert.Single(available.Tags);
            Assert.All(samples, s => Assert.Equal("identity_email", s.Tags["queue"]));
            foreach (var (state, count) in new[] { ("pending", pending), ("running", running) })
            {
                var sample = Assert.Single(samples, s => s.Name == "strataai.worker.identity.backlog" && Equals(s.Tags["state"], state));
                Assert.Equal(count, sample.Value); Assert.Equal(2, sample.Tags.Count);
            }
        }
        public void Dispose() => listener.Dispose();
    }
}
