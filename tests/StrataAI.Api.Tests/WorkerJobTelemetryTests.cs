using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Identity;
using StrataAI.Worker;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class WorkerJobTelemetryTests
{
    [Theory]
    [InlineData(JobProcessingResult.Completed, "completed")]
    [InlineData(JobProcessingResult.Retried, "retried")]
    [InlineData(JobProcessingResult.LeaseLost, "lease_lost")]
    [InlineData((JobProcessingResult)99, "unknown")]
    public void ARCH_08_Organization_outcomes_have_fixed_labels(JobProcessingResult result, string outcome)
    {
        using var services = Services(); using var capture = Capture.For(services);
        services.GetRequiredService<WorkerJobTelemetry>().Record(result);
        capture.AssertSingle("organization", outcome);
    }

    [Theory]
    [InlineData(IdentityDeliveryPass.Sent, "sent")]
    [InlineData(IdentityDeliveryPass.Cancelled, "cancelled")]
    [InlineData(IdentityDeliveryPass.Retried, "retried")]
    [InlineData(IdentityDeliveryPass.Failed, "failed")]
    [InlineData(IdentityDeliveryPass.LeaseLost, "lease_lost")]
    [InlineData((IdentityDeliveryPass)99, "unknown")]
    public void ARCH_08_Identity_outcomes_have_fixed_labels(IdentityDeliveryPass result, string outcome)
    {
        using var services = Services(); using var capture = Capture.For(services);
        services.GetRequiredService<WorkerJobTelemetry>().Record(result);
        capture.AssertSingle("identity_email", outcome);
    }

    [Fact]
    public void ARCH_08_Empty_polls_are_not_processed_jobs()
    {
        using var services = Services(); using var capture = Capture.For(services);
        var telemetry = services.GetRequiredService<WorkerJobTelemetry>();
        telemetry.Record(JobProcessingResult.Empty); telemetry.Record(IdentityDeliveryPass.Empty);
        Assert.Empty(capture.Samples);
    }

    [Fact]
    public void ARCH_08_Instrument_creation_failure_is_optional_for_both_queues()
    {
        using var factory = new BrokenFactory(); var telemetry = new WorkerJobTelemetry(factory);
        telemetry.Record(JobProcessingResult.Completed); telemetry.Record(IdentityDeliveryPass.Sent);
        Assert.Equal(2, factory.Calls);
    }

    internal static ServiceProvider Services() => new ServiceCollection().AddMetrics().AddSingleton<WorkerJobTelemetry>().BuildServiceProvider();
    private sealed class BrokenFactory : IMeterFactory
    {
        public int Calls { get; private set; }
        public Meter Create(MeterOptions options) { Calls++; throw new InvalidOperationException("private-instrument-failure"); }
        public void Dispose() { }
    }
    internal sealed class Capture : IDisposable
    {
        private readonly MeterListener listener = new();
        public ConcurrentQueue<(long Count, Dictionary<string, object?> Tags)> Samples { get; } = new();
        public static Capture For(IServiceProvider services, bool fail = false) =>
            new(services.GetRequiredService<IMeterFactory>().Create(WorkerJobTelemetry.MeterName), fail);
        private Capture(Meter meter, bool fail)
        {
            listener.InstrumentPublished = (instrument, observer) =>
            {
                if (ReferenceEquals(instrument.Meter, meter)) observer.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, count, tags, _) =>
            {
                Assert.Equal("strataai.worker.jobs", instrument.Name);
                Samples.Enqueue((count, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
                if (fail) throw new InvalidOperationException("private-listener-failure");
            });
            listener.Start();
        }
        public void AssertSingle(string queue, string outcome)
        {
            var sample = Assert.Single(Samples); Assert.Equal(1, sample.Count); Assert.Equal(2, sample.Tags.Count);
            Assert.Equal(queue, sample.Tags["queue"]); Assert.Equal(outcome, sample.Tags["outcome"]);
        }
        public void Dispose() => listener.Dispose();
    }
}
