using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Onboarding;

namespace StrataAI.Domain.Tests;

public sealed class InvitationRecipientAuthorityDeliveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T00:00:00Z");
    private static ClaimedBackgroundJob Claim(Guid source) => new(Guid.NewGuid(), Guid.NewGuid(),
        InvitationRecipientAuthorityDeliveryHandler.Type, Guid.NewGuid(), InvitationRecipientAuthorityDeliveryHandler.Service,
        "recipient-authority", JsonSerializer.Serialize(new { eventId = source }), 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(2));
    private static BackgroundJobProcessor Processor(Queue queue, Pages pages) => new(queue, new Clock(),
        [new InvitationRecipientAuthorityDeliveryHandler(pages)]);

    [Fact]
    public async Task PRD_60_Recipient_authority_dispatch_uses_original_lease_and_source_then_acknowledges_one_bounded_page()
    {
        var source = Guid.NewGuid(); var job = Claim(source); var queue = new Queue(job); var pages = new Pages();
        Assert.Equal(JobProcessingResult.Completed, await Processor(queue, pages).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Same(job, pages.Claim); Assert.Equal(source, pages.Source); Assert.Equal(100, pages.Limit);
        Assert.Equal(1, queue.Completed); Assert.Null(queue.Error);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"eventId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"eventId\":false}")]
    [InlineData("{\"eventId\":null,\"eventId\":null}")]
    [InlineData("{\"email\":\"private@example.test\"}")]
    public async Task PRD_60_Recipient_authority_invalid_or_private_reference_retries_without_storage_or_completion(string metadata)
    {
        var job = Claim(Guid.NewGuid()) with { SafeMetadataJson = metadata }; var queue = new Queue(job); var pages = new Pages();
        Assert.Equal(JobProcessingResult.Retried, await Processor(queue, pages).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Null(pages.Claim); Assert.Equal(0, queue.Completed); Assert.Equal("job_handler_failed", queue.Error);
        Assert.DoesNotContain("private", queue.Error!);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_60_Recipient_authority_unavailable_or_expired_lease_never_acknowledges(bool expired)
    {
        var job = Claim(Guid.NewGuid()); if (expired) job = job with { LeaseExpiresAt = Now.AddSeconds(4) };
        var queue = new Queue(job); var pages = new Pages { Available = false };
        Assert.Equal(expired ? JobProcessingResult.LeaseLost : JobProcessingResult.Retried,
            await Processor(queue, pages).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Equal(0, queue.Completed); if (expired) Assert.Null(pages.Claim);
    }
    [Fact]
    public async Task PRD_60_Recipient_authority_wrong_service_does_not_invoke_page_capability()
    {
        var job = Claim(Guid.NewGuid()) with { ServiceIdentity = "other" }; var queue = new Queue(job); var pages = new Pages();
        Assert.Equal(JobProcessingResult.Retried, await Processor(queue, pages).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Null(pages.Claim); Assert.Equal("job_service_denied", queue.Error); Assert.Equal(0, queue.Completed);
    }
    [Fact]
    public async Task PRD_60_Recipient_authority_cancellation_after_page_return_keeps_original_job_unacknowledged()
    {
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var job = Claim(Guid.NewGuid()); var queue = new Queue(job); var pages = new Pages { After = stopped.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(queue, pages).ProcessOneAsync(job.OrganizationId, job.WorkerId, stopped.Token));
        Assert.NotNull(pages.Claim); Assert.Equal(0, queue.Completed); Assert.Null(queue.Error);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class Pages : IInvitationRecipientAuthorityDeliveryStore
    {
        public bool Available { get; init; } = true;
        public Action? After { get; init; }
        public ClaimedBackgroundJob? Claim { get; private set; }
        public Guid Source { get; private set; }
        public int Limit { get; private set; }
        public Task<bool> DeliverNextPageAsync(ClaimedBackgroundJob job, Guid sourceEventId, int candidateLimit, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Claim = job; Source = sourceEventId; Limit = candidateLimit; After?.Invoke(); return Task.FromResult(Available); }
    }
    private sealed class Queue(ClaimedBackgroundJob job) : IBackgroundJobStore
    {
        public int Completed { get; private set; }
        public string? Error { get; private set; }
        public Task<ClaimedBackgroundJob?> ClaimAsync(Guid organizationId, Guid workerId, CancellationToken ct = default) => Task.FromResult<ClaimedBackgroundJob?>(job);
        public Task<bool> CompleteAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, CancellationToken ct = default)
        { Assert.Equal(job.OrganizationId, organizationId); Assert.Equal(job.Id, jobId); Assert.Equal(job.LeaseId, leaseId); Assert.Equal(job.WorkerId, workerId); Completed++; return Task.FromResult(true); }
        public Task<bool> FailAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, string errorCode, CancellationToken ct = default)
        { Error = errorCode; return Task.FromResult(true); }
    }
}
