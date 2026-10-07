using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

// Tests can replace this DI option to control committed work deterministically.
// Normal Demo startup enables execution; Production never registers this host.
public sealed record DemoOrganizationDeletionSimulationOptions(bool Enabled = true);

internal sealed class InMemoryOrganizationDeletionSimulation(InMemoryAccountOrganizationGate gate,
    DemoWorkTransactionScope scope, InMemoryOrganizationDeletionJobPublisher journal,
    IOrganizationDeletionGraphSimulation graph, InMemoryOrganizationStore organizations, IClock clock,
    IDemoOrganizationDeletionCompletionPublisher completion,
    IEnumerable<IDemoOrganizationTransactionParticipant> organizationParticipants,
    IEnumerable<IDemoWorkTransactionParticipant> workParticipants,
    ILogger<InMemoryOrganizationDeletionSimulation> logger) : IDemoOrganizationDeletionSimulation
{
    private Guid? _after;
    public async Task<bool> AdvanceAsync(CancellationToken cancellationToken = default)
    {
        await gate.Commands.WaitAsync(cancellationToken);
        try
        {
            await gate.WorkCommands.WaitAsync(cancellationToken);
            try
            {
                var next = journal.NextPending(_after); if (next is null) return false;
                var (organization, actor, root) = next.Value; _after = organization;
                using var owned = scope.Enter(organization, organizationCommand: true,
                    deletionRequest: root.RequestId, deletionActor: actor);
                var rollback = organizationParticipants.Select(p => p.CaptureRollback())
                    .Concat(workParticipants.Select(p => p.CaptureRollback())).ToArray();
                var committed = false;
                try
                {
                    // No browser session is borrowed. Authority is the immutable
                    // request committed by the original Owner in the publisher.
                    while (await graph.ApplyPageAsync(organization, actor, root.RequestId, root.AcceptedVersion,
                        OrganizationDeletionJobs.PageSize, cancellationToken) != 0) cancellationToken.ThrowIfCancellationRequested();
                    var parent = await organizations.FindOrganizationAsync(organization, cancellationToken);
                    if (parent is not { Status: OrganizationStatus.Deleting } || parent.Version != root.AcceptedVersion)
                        throw new OrganizationDeletionPublicationUnavailableException();
                    var at = parent.UpdatedAt > clock.UtcNow ? parent.UpdatedAt : clock.UtcNow;
                    var source = new OrganizationMetadataEvent(Guid.NewGuid(), "ORGANIZATION_DELETED", actor,
                        organization, checked(root.AcceptedVersion + 1), at);
                    organizations.CompleteAcceptedDeletion(organization, actor, root.RequestId, root.AcceptedVersion, at);
                    await organizations.AppendAuditAsync(organization, actor, "ORGANIZATION_DELETED", "Organization",
                        organization, journal.ReadCorrelation(organization, actor, root.RequestId), cancellationToken);
                    await completion.PublishAsync(organization, actor, root.RequestId, source, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    committed = true; return true;
                }
                catch (Exception)
                {
                    logger.LogWarning("Demo Organization deletion rolled back. OrganizationId={OrganizationId}; CorrelationId={CorrelationId}",
                        organization, journal.ReadCorrelation(organization, actor, root.RequestId));
                    throw;
                }
                finally { if (!committed) foreach (var restore in rollback.Reverse()) restore(); }
            }
            finally { gate.WorkCommands.Release(); }
        }
        finally { gate.Commands.Release(); }
    }
}

internal sealed class InMemoryOrganizationDeletionCompletionPublisher(InMemoryOrganizationStore organizations,
    InMemoryOrganizationDeletionJobPublisher journal) : IDemoOrganizationDeletionCompletionPublisher
{
    public async Task PublishAsync(Guid organizationId, Guid actorId, Guid requestId,
        OrganizationMetadataEvent source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parent = await organizations.FindOrganizationAsync(organizationId, cancellationToken);
        var audit = organizations.ReadDeletionAudit(organizationId, actorId, requestId);
        if (parent is not { Status: OrganizationStatus.Deleted } || parent.Version != source.Version
            || parent.UpdatedAt != source.CreatedAt || audit is null || audit.ActorId != actorId
            || audit.EntityType != "Organization" || audit.EntityId != organizationId || audit.EventType != "ORGANIZATION_DELETED"
            || audit.CreatedAt != source.CreatedAt || audit.CorrelationId != journal.ReadCorrelation(organizationId, actorId, requestId)
            || !organizations.MatchesDeletionAttribution(organizationId, actorId, source.CreatedAt))
            throw new OrganizationDeletionPublicationUnavailableException();
        journal.Complete(organizationId, actorId, requestId, source);
    }
}

internal sealed class DemoOrganizationDeletionSimulationHost(IDemoOrganizationDeletionSimulation simulation,
    DemoOrganizationDeletionSimulationOptions options, ILogger<DemoOrganizationDeletionSimulationHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Enabled && await simulation.AdvanceAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Preserve pending work for another attempt without logging
                // private records, request contents or exception diagnostics.
                logger.LogWarning("Demo Organization deletion simulation rolled back; retry remains pending.");
            }
            try { await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
