using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.Organizations;

public interface IOrganizationMetadataScopeReader
{
    Task<IReadOnlyList<Guid>> ReadAsync(Guid? after, int limit, CancellationToken cancellationToken);
}

public interface IOrganizationMetadataDeliveryStore
{
    Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid eventId, CancellationToken cancellationToken);
}

public sealed class OrganizationMetadataDeliveryHandler(IOrganizationMetadataDeliveryStore store) : IBackgroundJobHandler
{
    public const string Type = "ORGANIZATION_METADATA_EVENT_READY";
    public const string Service = "organization-metadata-delivery";
    public string JobType => Type;
    public string ServiceIdentity => Service;
    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (job.JobType != Type || job.ServiceIdentity != Service || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty
            || job.Id == Guid.Empty || job.LeaseId == Guid.Empty || job.WorkerId == Guid.Empty)
            throw new InvalidOperationException("Organization metadata delivery scope is invalid.");
        // Both delivery contracts use exactly one canonical event UUID.
        var eventId = OrganizationLifecycleDeliveryHandler.ParseEventId(job.SafeMetadataJson);
        if (!await store.MarkReadyAsync(job, eventId, cancellationToken))
            throw new InvalidOperationException("Organization metadata event is unavailable.");
    }
}
