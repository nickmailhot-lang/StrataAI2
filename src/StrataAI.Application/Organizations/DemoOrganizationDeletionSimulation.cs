namespace StrataAI.Application.Organizations;

// Demo provider only. Processes a committed request independently of browser
// session lifetime. Production uses its durable separately leased Worker.
public interface IDemoOrganizationDeletionSimulation
{
    Task<bool> AdvanceAsync(CancellationToken cancellationToken = default);
}

public interface IDemoOrganizationDeletionCompletionPublisher
{
    Task PublishAsync(Guid organizationId, Guid actorId, Guid requestId,
        OrganizationMetadataEvent source, CancellationToken cancellationToken = default);
}
