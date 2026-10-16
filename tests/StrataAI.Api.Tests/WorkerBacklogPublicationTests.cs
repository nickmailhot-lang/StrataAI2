using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Identity;
using StrataAI.Worker;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class WorkerBacklogPublicationTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ARCH_08_Failed_backlog_publication_preserves_independent_gauge_and_sampling(bool identity, bool failCounts)
    {
        using var meter = new Meter(WorkerJobTelemetry.MeterName);
        using var factory = new Factory(meter);
        var queue = identity ? "identity_email" : "organization";
        var countsName = identity ? "strataai.worker.identity.backlog" : "strataai.worker.scoped.backlog";
        var failedName = failCounts ? countsName : countsName + ".available";
        var healthyName = failCounts ? countsName + ".available" : countsName;
        var publicationFailures = 0;
        using var publication = new MeterListener();
        publication.InstrumentPublished = (instrument, _) =>
        {
            if (ReferenceEquals(instrument.Meter, meter) && instrument.Name == failedName)
            {
                publicationFailures++;
                throw new InvalidOperationException("private-publication-failure");
            }
        };
        publication.Start();
        var reads = 0;
        var failRead = false;
        Task<OrganizationJobBacklog> OrganizationRead(Guid _, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); reads++;
            return failRead ? Task.FromException<OrganizationJobBacklog>(new InvalidOperationException("private-reader"))
                : Task.FromResult(new OrganizationJobBacklog(2, 3));
        }
        Task<IdentityDeliveryBacklog> IdentityRead(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); reads++;
            return failRead ? Task.FromException<IdentityDeliveryBacklog>(new InvalidOperationException("private-reader"))
                : Task.FromResult(new IdentityDeliveryBacklog(2, 3));
        }
        using BackgroundService worker = identity
            ? new IdentityEmailBacklogWorker(new IdentityReader(IdentityRead), factory)
            : new OrganizationBacklogWorker(new OrganizationReader(OrganizationRead), new([Guid.NewGuid()]), factory);
        Assert.Equal(1, publicationFailures);
        publication.Dispose();
        using var capture = new MeterListener();
        var samples = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        capture.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter) && instrument.Name == healthyName)
            {
                Assert.Equal(failCounts ? null : "{job}", instrument.Unit);
                listener.EnableMeasurementEvents(instrument);
            }
        };
        capture.SetMeasurementEventCallback<long>((_, value, tags, _) => samples.Add((value, tags.ToArray())));
        capture.Start();
        void Observe(bool available)
        {
            samples.Clear(); capture.RecordObservableInstruments();
            if (failCounts)
            {
                var sample = Assert.Single(samples);
                Assert.Equal(available ? 1 : 0, sample.Value);
                var tag = Assert.Single(sample.Tags); Assert.Equal("queue", tag.Key); Assert.Equal(queue, tag.Value);
            }
            else if (!available) Assert.Empty(samples);
            else
            {
                Assert.Equal(2, samples.Count);
                foreach (var (state, count) in new[] { ("pending", 2L), ("running", 3L) })
                {
                    var sample = Assert.Single(samples, sample => sample.Tags.Any(tag => tag.Key == "state" && Equals(tag.Value, state)));
                    Assert.Equal(count, sample.Value); Assert.Equal(2, sample.Tags.Length);
                    Assert.Contains(new KeyValuePair<string, object?>("queue", queue), sample.Tags);
                }
            }
        }
        Task Sample() => worker is IdentityEmailBacklogWorker mail
            ? mail.SampleAsync(TestContext.Current.CancellationToken)
            : ((OrganizationBacklogWorker)worker).SampleAsync(TestContext.Current.CancellationToken);
        Observe(false); Assert.Equal(0, reads);
        await Sample(); Observe(true); Assert.Equal(1, reads);
        Observe(true); Assert.Equal(1, reads); // Export remains read-only.
        failRead = true; await Sample(); Observe(false); Assert.Equal(2, reads);
        failRead = false; await Sample(); Observe(true); Assert.Equal(3, reads);
        Assert.Equal(1, publicationFailures);
    }

    private sealed class Factory(Meter meter) : IMeterFactory
    {
        public Meter Create(MeterOptions options) => meter;
        public void Dispose() { }
    }
    private sealed class OrganizationReader(Func<Guid, CancellationToken, Task<OrganizationJobBacklog>> read) : IOrganizationJobBacklogReader
    {
        public Task<OrganizationJobBacklog> ReadAsync(Guid organizationId, CancellationToken cancellationToken) => read(organizationId, cancellationToken);
    }
    private sealed class IdentityReader(Func<CancellationToken, Task<IdentityDeliveryBacklog>> read) : IIdentityDeliveryBacklogReader
    {
        public Task<IdentityDeliveryBacklog> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
