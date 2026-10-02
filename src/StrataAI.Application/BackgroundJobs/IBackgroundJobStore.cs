namespace StrataAI.Application.BackgroundJobs;

public sealed record NewBackgroundJob(
    Guid Id, Guid OrganizationId, string JobType, string IdempotencyKey,
    Guid ActorId, string ServiceIdentity, string CorrelationId, string SafeMetadataJson)
{
    public DateTimeOffset? AvailableAt { get; init; }
}

public sealed record ClaimedBackgroundJob(
    Guid Id, Guid OrganizationId, string JobType, Guid ActorId,
    string ServiceIdentity, string CorrelationId, string SafeMetadataJson,
    int AttemptCount, Guid LeaseId, Guid WorkerId, DateTimeOffset LeaseExpiresAt);

public interface IBackgroundJobStore
{
    Task<ClaimedBackgroundJob?> ClaimAsync(Guid organizationId, Guid workerId, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, CancellationToken cancellationToken = default);
    Task<bool> FailAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, string errorCode, CancellationToken cancellationToken = default);
}
