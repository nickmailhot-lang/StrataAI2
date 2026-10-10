using StrataAI.Application.BackgroundJobs;

namespace StrataAI.Application.Organizations;

public interface IOrganizationConfigurationScopeReader
{
    Task<IReadOnlyList<Guid>> ReadAsync(Guid? after, int limit, CancellationToken cancellationToken);
}

public interface IOrganizationConfigurationDeliveryStore
{
    Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid eventId, CancellationToken cancellationToken);
}

public sealed class OrganizationConfigurationDeliveryHandler(IOrganizationConfigurationDeliveryStore store) : IBackgroundJobHandler
{
    public const string Type = "ORGANIZATION_CONFIGURATION_EVENT_READY";
    public const string Service = "organization-configuration-delivery";
    public string JobType => Type;
    public string ServiceIdentity => Service;
    public async Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (job.JobType != Type || job.ServiceIdentity != Service || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty
            || job.Id == Guid.Empty || job.LeaseId == Guid.Empty || job.WorkerId == Guid.Empty)
            throw new InvalidOperationException("Organization configuration delivery scope is invalid.");
        // Durable delivery carries exactly one canonical, body-free event UUID.
        var eventId = OrganizationLifecycleDeliveryHandler.ParseEventId(job.SafeMetadataJson);
        if (!await store.MarkReadyAsync(job, eventId, cancellationToken))
            throw new InvalidOperationException("Organization configuration event is unavailable.");
    }
}
