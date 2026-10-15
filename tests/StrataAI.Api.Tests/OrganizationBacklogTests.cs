using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Worker;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class OrganizationBacklogTests
{
    [Fact]
    public async Task ARCH_08_Scoped_backlog_is_read_only_aggregated_and_has_no_private_labels()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var store = new Reader((id, _) => Task.FromResult(id == first ? new OrganizationJobBacklog(2, 3) : new(5, 7)));
        using var worker = new OrganizationBacklogWorker(store, new([first, second, first]), services.GetRequiredService<IMeterFactory>());
        using var capture = new Capture(services);
        capture.Read(); capture.AssertUnavailable(); // No invented initial zero.
        await worker.SampleAsync(TestContext.Current.CancellationToken);
        capture.Read(); capture.AssertCounts(7, 10);
        Assert.Equal([first, second], store.Calls);
        capture.Read(); capture.AssertCounts(7, 10); Assert.Equal(2, store.Calls.Count); // Export never reads the DB.
    }

    [Theory]
    [InlineData("exception")]
    [InlineData("cancellation")]
    [InlineData("negative")]
    [InlineData("overflow")]
    public async Task ARCH_08_Failed_or_partial_observation_never_reports_zero_or_previous_success(string failure)
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var fail = false;
        var store = new Reader((id, _) => !fail ? Task.FromResult(new OrganizationJobBacklog(2, 3))
            : failure == "overflow" ? Task.FromResult(new OrganizationJobBacklog(long.MaxValue, 0))
            : id == first ? Task.FromResult(new OrganizationJobBacklog(9, 9))
            : failure == "negative" ? Task.FromResult(new OrganizationJobBacklog(-1, 0))
            : Task.FromException<OrganizationJobBacklog>(failure == "cancellation"
                ? new OperationCanceledException("private-database") : new InvalidOperationException("private-database")));
        using var worker = new OrganizationBacklogWorker(store, new([first, second]), services.GetRequiredService<IMeterFactory>());
        using var capture = new Capture(services);
        await worker.SampleAsync(TestContext.Current.CancellationToken); capture.Read(); capture.AssertCounts(4, 6);
        fail = true;
        await worker.SampleAsync(TestContext.Current.CancellationToken); capture.Read(); capture.AssertUnavailable();
        fail = false;
        await worker.SampleAsync(TestContext.Current.CancellationToken); capture.Read(); capture.AssertCounts(4, 6);
    }

    [Fact]
    public async Task ARCH_08_Empty_queue_is_successful_and_sampler_shutdown_is_cancellable()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Reader((_, _) => { read.TrySetResult(); return Task.FromResult(new OrganizationJobBacklog(0, 0)); });
        using var worker = new OrganizationBacklogWorker(store, new([Guid.NewGuid()]), services.GetRequiredService<IMeterFactory>());
        using var capture = new Capture(services);
        await worker.SampleAsync(TestContext.Current.CancellationToken);
        capture.Read(); capture.AssertCounts(0, 0); store.Calls.Clear();
        read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try { await read.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); }
        finally { await worker.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); }
        capture.Read(); capture.AssertCounts(0, 0); Assert.Single(store.Calls);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    public void ARCH_08_Observer_refuses_unbounded_or_missing_scope(int count)
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        Guid[] ids = count < 0 ? [Guid.Empty] : Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Throws<ArgumentException>(() => new OrganizationBacklogWorker(new Reader((_, _) => throw new Exception()),
            new(ids), services.GetRequiredService<IMeterFactory>()));
    }

    [Fact]
    public async Task ARCH_08_Whole_sweep_deadline_refuses_partial_counts()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var store = new Reader(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new(9, 9); });
        using var worker = new OrganizationBacklogWorker(store, new([Guid.NewGuid(), Guid.NewGuid()]), services.GetRequiredService<IMeterFactory>());
        using var capture = new Capture(services);
        var clock = Stopwatch.StartNew();
        await worker.SampleAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(7));
        capture.Read(); capture.AssertUnavailable(); Assert.Single(store.Calls);
    }

    [Fact]
    public async Task ARCH_08_Stop_cancels_inflight_read_and_precancelled_sample_does_not_read()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Reader(async (_, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new(0, 0); });
        using var worker = new OrganizationBacklogWorker(store, new([Guid.NewGuid()]), services.GetRequiredService<IMeterFactory>());
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); }
        finally { await worker.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); }
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully); Assert.Single(store.Calls);
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await stopped.CancelAsync(); await worker.SampleAsync(stopped.Token); Assert.Single(store.Calls);
    }

    [Fact]
    public async Task ARCH_08_Optional_instrument_failure_does_not_prevent_sampling()
    {
        using var factory = new BrokenFactory();
        var store = new Reader((_, _) => Task.FromResult(new OrganizationJobBacklog(2, 1)));
        using var worker = new OrganizationBacklogWorker(store, new([Guid.NewGuid()]), factory);
        await worker.SampleAsync(TestContext.Current.CancellationToken); Assert.Single(store.Calls);
    }

    private sealed class BrokenFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => throw new InvalidOperationException("private-instrument");
        public void Dispose() { }
    }

    [Fact]
    public async Task ARCH_08_Backlog_handshake_has_its_own_bounded_diagnostic_connection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var reader = new PostgresOrganizationJobBacklogReader(
            $"Host=127.0.0.1;Port={port};Database=fixture;Username=fixture;Password=fixture;SSL Mode=Disable;Timeout=30;Command Timeout=30");
        var clock = Stopwatch.StartNew();
        var sample = reader.ReadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => sample.WaitAsync(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken));
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
    }

    internal sealed class Reader(Func<Guid, CancellationToken, Task<OrganizationJobBacklog>> read) : IOrganizationJobBacklogReader
    {
        public List<Guid> Calls { get; } = [];
        public Task<OrganizationJobBacklog> ReadAsync(Guid id, CancellationToken ct) { Calls.Add(id); return read(id, ct); }
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
                if (ReferenceEquals(instrument.Meter, meter) && instrument.Name.StartsWith("strataai.worker.scoped.backlog", StringComparison.Ordinal))
                    observer.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => samples.Add((instrument.Name, value,
                tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
            listener.Start();
        }
        public void Read() { samples.Clear(); listener.RecordObservableInstruments(); }
        public void AssertUnavailable()
        {
            var only = Assert.Single(samples); Assert.Equal("strataai.worker.scoped.backlog.available", only.Name);
            Assert.Equal(0, only.Value); Assert.Single(only.Tags); Assert.Equal("organization", only.Tags["queue"]);
        }
        public void AssertCounts(long pending, long running)
        {
            Assert.Equal(3, samples.Count);
            var available = Assert.Single(samples, s => s.Name.EndsWith(".available", StringComparison.Ordinal));
            Assert.Equal(1, available.Value); Assert.Single(available.Tags);
            Assert.All(samples, s => Assert.Equal("organization", s.Tags["queue"]));
            foreach (var (state, count) in new[] { ("pending", pending), ("running", running) })
            {
                var sample = Assert.Single(samples, s => s.Name == "strataai.worker.scoped.backlog" && Equals(s.Tags["state"], state));
                Assert.Equal(count, sample.Value); Assert.Equal(2, sample.Tags.Count);
            }
        }
        public void Dispose() => listener.Dispose();
    }
}
