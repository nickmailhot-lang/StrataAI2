using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.Organizations;

public interface IOrganizationOperationalLifecycleScopeReader
{
    Task<IReadOnlyList<Guid>> ReadAsync(Guid? after, int limit, CancellationToken cancellationToken);
}

public interface IOrganizationOperationalLifecycleDeliveryStore
{
    // Storage must bind this exact live lease to the immutable owning transition.
    // Retained source attribution is not a current user/session access grant.
    Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid eventId, CancellationToken cancellationToken);
}

public sealed class OrganizationOperationalLifecycleDeliveryHandler(IOrganizationOperationalLifecycleDeliveryStore store)
    : IBackgroundJobHandler
{
    public const string Type = "ORGANIZATION_OPERATIONAL_LIFECYCLE_EVENT_READY";
    public const string Service = "organization-operational-lifecycle-delivery";
    public string JobType => Type;
    public string ServiceIdentity => Service;

    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (job.JobType != Type || job.ServiceIdentity != Service || job.OrganizationId == Guid.Empty
            || job.ActorId == Guid.Empty || job.Id == Guid.Empty || job.LeaseId == Guid.Empty || job.WorkerId == Guid.Empty)
            throw new InvalidOperationException("Operational lifecycle delivery scope is invalid.");
        var eventId = OrganizationLifecycleDeliveryHandler.ParseEventId(job.SafeMetadataJson);
        if (!await store.MarkReadyAsync(job, eventId, cancellationToken))
            throw new InvalidOperationException("Operational lifecycle event is unavailable.");
        cancellationToken.ThrowIfCancellationRequested();
    }
}
