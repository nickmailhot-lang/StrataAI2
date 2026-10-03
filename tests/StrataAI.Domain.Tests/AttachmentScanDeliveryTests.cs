using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentScanDeliveryTests
{
    private static readonly byte[] Bytes="private test bytes"u8.ToArray();
    private static (ClaimedBackgroundJob Job, AttachmentScanRequest Request) Claim()
    {
        var reference=new AttachmentObjectReference(Guid.NewGuid(),Guid.NewGuid()); var card=Guid.NewGuid();
        return (new(Guid.NewGuid(),reference.OrganizationId,AttachmentScanJobs.Type,Guid.NewGuid(),AttachmentScanJobs.Service,"scan-handler",
            JsonSerializer.Serialize(new {attachmentId=reference.AttachmentId,cardId=card,version=1}),1,Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow.AddMinutes(2)),
            new(reference,Bytes.Length,Convert.ToHexStringLower(SHA256.HashData(Bytes))));
    }
    [Theory]
    [InlineData(AttachmentScannerVerdict.Clean,AttachmentScanStatus.Clean)]
    [InlineData(AttachmentScannerVerdict.Infected,AttachmentScanStatus.Rejected)]
    [InlineData(AttachmentScannerVerdict.Unavailable,AttachmentScanStatus.Failed)]
    public async Task Only_full_integrity_bound_evidence_is_submitted_after_current_claim_admission(AttachmentScannerVerdict verdict,AttachmentScanStatus expected)
    {
        var ct=TestContext.Current.CancellationToken; var (job,request)=Claim(); var delivery=new Delivery(new(AttachmentScanLoadStatus.Ready,request));
        var storage=new Storage(); var provider=new Provider(verdict); var handler=new AttachmentScanDeliveryHandler(delivery,new(storage,provider));
        await handler.ExecuteAsync(job,ct);
        Assert.Equal(1,delivery.LoadCalls); Assert.Equal(1,storage.Opens); Assert.Equal(1,provider.Calls); Assert.Equal(1,delivery.FinishCalls);
        Assert.Equal(request,delivery.Evidence!.Request); Assert.Equal(expected,delivery.Evidence.Status); Assert.True(storage.Disposed);
        Assert.Equal(AttachmentScanAttempt.Parse(job.SafeMetadataJson),delivery.Attempt); Assert.Equal(job,delivery.Claim);
    }
    [Theory]
    [InlineData(AttachmentScanLoadStatus.Applied)] [InlineData(AttachmentScanLoadStatus.Superseded)]
    public async Task Committed_or_superseded_work_has_no_provider_read_or_second_effect(AttachmentScanLoadStatus status)
    {
        var ct=TestContext.Current.CancellationToken; var (job,_)=Claim(); var delivery=new Delivery(new(status)); var storage=new Storage(); var provider=new Provider(AttachmentScannerVerdict.Clean);
        await new AttachmentScanDeliveryHandler(delivery,new(storage,provider)).ExecuteAsync(job,ct);
        Assert.Equal(1,delivery.LoadCalls); Assert.Equal(0,delivery.FinishCalls); Assert.Equal(0,storage.Opens); Assert.Equal(0,provider.Calls);
    }
    [Theory]
    [InlineData("lease_lost")][InlineData("no_request")][InlineData("foreign_tenant")][InlineData("foreign_attachment")]
    [InlineData("unexpected_payload")][InlineData("unknown_status")]
    public async Task Invalid_or_unproven_loads_never_read_private_object_bytes(string mutation)
    {
        var ct=TestContext.Current.CancellationToken; var (job,request)=Claim(); var loaded=new AttachmentScanLoad(AttachmentScanLoadStatus.Ready,request);
        loaded=mutation switch
        {
            "lease_lost" => new(AttachmentScanLoadStatus.LeaseLost), "no_request" => new(AttachmentScanLoadStatus.Ready),
            "foreign_tenant" => new(AttachmentScanLoadStatus.Ready,new(new(Guid.NewGuid(),request.Reference.AttachmentId),request.SizeBytes,request.Sha256)),
            "foreign_attachment" => new(AttachmentScanLoadStatus.Ready,new(new(job.OrganizationId,Guid.NewGuid()),request.SizeBytes,request.Sha256)),
            "unexpected_payload" => new(AttachmentScanLoadStatus.Superseded,request),
            "unknown_status" => new((AttachmentScanLoadStatus)99), _ => throw new InvalidOperationException()
        };
        var delivery=new Delivery(loaded); var storage=new Storage(); var provider=new Provider(AttachmentScannerVerdict.Clean);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(() => new AttachmentScanDeliveryHandler(delivery,new(storage,provider)).ExecuteAsync(job,ct));
        Assert.Equal("Attachment scan delivery is unavailable.",error.Message); Assert.Equal(0,storage.Opens); Assert.Equal(0,provider.Calls); Assert.Equal(0,delivery.FinishCalls);
    }
    [Theory]
    [InlineData("type")][InlineData("service")][InlineData("job")][InlineData("tenant")][InlineData("actor")][InlineData("worker")][InlineData("lease")][InlineData("attempt")]
    public async Task Invalid_job_envelopes_are_refused_before_private_load_or_scan(string mutation)
    {
        var ct=TestContext.Current.CancellationToken; var (job,request)=Claim(); job=mutation switch
        {
            "type" => job with {JobType="OTHER"}, "service" => job with {ServiceIdentity="other"}, "job" => job with {Id=Guid.Empty},
            "tenant" => job with {OrganizationId=Guid.Empty}, "actor" => job with {ActorId=Guid.Empty}, "worker" => job with {WorkerId=Guid.Empty},
            "lease" => job with {LeaseId=Guid.Empty}, "attempt" => job with {AttemptCount=0}, _ => throw new InvalidOperationException()
        };
        var delivery=new Delivery(new(AttachmentScanLoadStatus.Ready,request)); var storage=new Storage(); var provider=new Provider(AttachmentScannerVerdict.Clean);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AttachmentScanDeliveryHandler(delivery,new(storage,provider)).ExecuteAsync(job,ct));
        Assert.Equal(0,delivery.LoadCalls); Assert.Equal(0,storage.Opens); Assert.Equal(0,provider.Calls); Assert.Equal(0,delivery.FinishCalls);
    }
    [Theory]
    [InlineData(AttachmentScanCompletion.LeaseLost)][InlineData(AttachmentScanCompletion.Retry)]
    public async Task A_completed_scan_is_not_a_committed_verdict_when_the_database_requests_retry_or_loses_lease(AttachmentScanCompletion outcome)
    {
        var ct=TestContext.Current.CancellationToken; var (job,request)=Claim(); var delivery=new Delivery(new(AttachmentScanLoadStatus.Ready,request)) {Outcome=outcome};
        var storage=new Storage(); var provider=new Provider(AttachmentScannerVerdict.Unavailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AttachmentScanDeliveryHandler(delivery,new(storage,provider)).ExecuteAsync(job,ct));
        Assert.Equal(1,delivery.FinishCalls); Assert.Equal(AttachmentScanStatus.Failed,delivery.Evidence!.Status); Assert.True(storage.Disposed);
    }
    [Fact]
    public async Task Shutdown_between_byte_read_and_completion_cannot_record_a_verdict()
    {
        var (job,request)=Claim(); using var cancel=new CancellationTokenSource(); var delivery=new Delivery(new(AttachmentScanLoadStatus.Ready,request));
        var storage=new Storage(); var provider=new Provider(AttachmentScannerVerdict.Clean) {AfterRead=cancel.Cancel};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AttachmentScanDeliveryHandler(delivery,new(storage,provider)).ExecuteAsync(job,cancel.Token));
        Assert.Equal(0,delivery.FinishCalls); Assert.True(storage.Disposed);
    }
    private sealed class Delivery(AttachmentScanLoad load) : IAttachmentScanDeliveryStore
    {
        public int LoadCalls {get;private set;} public int FinishCalls {get;private set;}
        public AttachmentScanCompletion Outcome {get;init;}=AttachmentScanCompletion.Applied;
        public AttachmentScanEvidence? Evidence {get;private set;} public AttachmentScanAttempt? Attempt {get;private set;} public ClaimedBackgroundJob? Claim {get;private set;}
        public Task<AttachmentScanLoad> LoadAsync(ClaimedBackgroundJob job,AttachmentScanAttempt attempt,CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); LoadCalls++; return Task.FromResult(load); }
        public Task<AttachmentScanCompletion> FinishAsync(ClaimedBackgroundJob job,AttachmentScanAttempt attempt,AttachmentScanEvidence evidence,CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); FinishCalls++; Claim=job; Attempt=attempt; Evidence=evidence; return Task.FromResult(Outcome); }
    }
    private sealed class Provider(AttachmentScannerVerdict verdict) : IAttachmentMalwareScanner
    {
        public int Calls {get;private set;} public Action? AfterRead {get;init;}
        public async Task<AttachmentScannerVerdict> ScanAsync(Stream source,CancellationToken ct)
        { Calls++; await source.CopyToAsync(Stream.Null,ct); AfterRead?.Invoke(); return verdict; }
    }
    private sealed class Storage : IAttachmentObjectStorage
    {
        public int Opens {get;private set;} public bool Disposed {get;private set;}
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference,CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Opens++; return Task.FromResult<Stream?>(new TrackedStream(() => Disposed=true)); }
        public Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference,Stream source,long max,CancellationToken ct) => throw new InvalidOperationException("Scan cannot upload.");
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference,CancellationToken ct) => throw new InvalidOperationException("Scan cannot delete.");
        private sealed class TrackedStream(Action disposed) : MemoryStream(Bytes,false)
        { protected override void Dispose(bool disposing) {base.Dispose(disposing);if(disposing)disposed();} }
    }
}
