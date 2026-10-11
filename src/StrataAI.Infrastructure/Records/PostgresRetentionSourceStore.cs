using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Records;

// Unregistered. Immutable classification and trigger-owned safe audit share the
// existing Organization transaction. No automatic producer classification.
internal sealed class PostgresRetentionSourceStore(PostgresConnectionFactory connections,
    IOrganizationStore organizations, IIdentityStore identities, ICommandActorAuthorization actors,
    RetentionProducerAuthorization authorization, IClock clock) : IRetentionSourceStore
{
    private readonly PostgresRetentionAuthorizationReferences references = new(connections);
    public Task<RetentionSourceReference?> ReadSourceAsync(Guid organization, RetentionRecordClass recordClass, Guid record, CancellationToken ct) =>
        references.ReadSourceAsync(organization, recordClass, record, ct);

    public async Task<RetentionSourceReference?> CaptureAsync(RetentionSourceWrite write, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (write.OrganizationId == Guid.Empty || !connections.HasCommandScope(write.OrganizationId))
            throw new InvalidOperationException("Retention classification requires an owning transaction.");
        var review = write.Review;
        var expectedClass = write.Kind == RetentionProducerKind.Attachment ? RetentionRecordClass.Document : RetentionRecordClass.OperationalWork;
        if (write.Kind is not (RetentionProducerKind.Card or RetentionProducerKind.CardComment or RetentionProducerKind.Attachment)
            || write.RecordClass != expectedClass || review is null || review.OrganizationId != write.OrganizationId
            || review.RecordId != write.RecordId || review.Kind != write.Kind
            || review.SourceVersion != write.SourceVersion || review.CardVersion != write.CardVersion
            || RecordRetentionRules.Validate(new RetentionPolicyApplicability(write.OrganizationId, write.RecordClass, write.Category, write.Jurisdiction)) is not null
            || write.EventId == Guid.Empty || string.IsNullOrWhiteSpace(write.CorrelationId) || write.CorrelationId.Length > 256 || write.CorrelationId.Any(char.IsControl)
            || write.RegisteredAt.Offset != TimeSpan.Zero || write.RegisteredAt.Ticks % 10 != 0 || write.RegisteredAt > clock.UtcNow
            || write.RegisteredAt < review.CreatedAt || !await authorization.VerifyAsync(review, write.ActorId, ct)) return null;
        var parent = await organizations.FindOrganizationAsync(write.OrganizationId, ct);
        var member = await organizations.FindMembershipAsync(write.OrganizationId, write.ActorId, ct);
        var subject = await identities.FindUserByIdAsync(write.ActorId, ct);
        if (parent is not { Status: OrganizationStatus.Active } || subject is not { Status: AccountStatus.Active }
            || member is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            || member.Id != write.MembershipId || member.Version != write.MembershipVersion || member.Role != write.Capacity
            || write.RegisteredAt < parent.UpdatedAt || write.RegisteredAt < member.UpdatedAt || write.RegisteredAt < subject.UpdatedAt
            || string.IsNullOrWhiteSpace(subject.DisplayName) || subject.DisplayName.Length > 160 || subject.DisplayName.Any(char.IsControl)
            || !await actors.VerifyAsync(write.ActorId, ct)
            || await references.ReadSourceAsync(write.OrganizationId, write.RecordClass, write.RecordId, ct) is not null) return null;
        await using var session = await connections.OpenTenantSessionAsync(write.OrganizationId, ct);
        await using var begin = new NpgsqlCommand("SAVEPOINT retention_source_write;", session.Connection, session.Transaction);
        await begin.ExecuteNonQueryAsync(ct);
        try
        {
            await using var context = new NpgsqlCommand("SELECT set_config('app.identity_subject',@actor,true);", session.Connection, session.Transaction);
            context.Parameters.AddWithValue("actor", write.ActorId.ToString()); await context.ExecuteScalarAsync(ct);
            await using var insert = new NpgsqlCommand("""
                INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,parent_card_id,category,jurisdiction,
                    registered_at,actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id)
                VALUES(@tenant,@class,@record,@kind,@parent,@category,@jurisdiction,@at,@actor,@member,@memberVersion,@label,@capacity,@event,@correlation);
                """, session.Connection, session.Transaction);
            insert.Parameters.AddWithValue("tenant", write.OrganizationId);
            insert.Parameters.AddWithValue("class", write.RecordClass == RetentionRecordClass.Document ? "DOCUMENT" : "OPERATIONAL_WORK");
            insert.Parameters.AddWithValue("record", write.RecordId);
            insert.Parameters.AddWithValue("kind", write.Kind == RetentionProducerKind.Card ? "CARD" : write.Kind == RetentionProducerKind.CardComment ? "CARD_COMMENT" : "ATTACHMENT");
            insert.Parameters.AddWithValue("parent", NpgsqlDbType.Uuid, (object?)review.ParentCardId ?? DBNull.Value);
            insert.Parameters.AddWithValue("category", write.Category);
            insert.Parameters.AddWithValue("jurisdiction", NpgsqlDbType.Text, (object?)write.Jurisdiction ?? DBNull.Value);
            insert.Parameters.AddWithValue("at", write.RegisteredAt); insert.Parameters.AddWithValue("actor", write.ActorId);
            insert.Parameters.AddWithValue("member", write.MembershipId); insert.Parameters.AddWithValue("memberVersion", write.MembershipVersion);
            insert.Parameters.AddWithValue("label", subject.DisplayName);
            insert.Parameters.AddWithValue("capacity", write.Capacity == OrganizationRole.Owner ? "ORGANIZATION_OWNER" : "ORGANIZATION_ADMIN");
            insert.Parameters.AddWithValue("event", write.EventId); insert.Parameters.AddWithValue("correlation", write.CorrelationId);
            await insert.ExecuteNonQueryAsync(ct);
            var captured = await references.ReadSourceAsync(write.OrganizationId, write.RecordClass, write.RecordId, ct);
            var expected = new RetentionSourceReference(write.OrganizationId, write.RecordClass, write.RecordId, write.Kind,
                review.ParentCardId, 1, review.BoardId, review.SourceVersion);
            if (captured != expected || !await authorization.VerifyAsync(review, write.ActorId, ct))
            { await Rollback(session, ct); return null; }
            await using var release = new NpgsqlCommand("RELEASE SAVEPOINT retention_source_write;", session.Connection, session.Transaction);
            await release.ExecuteNonQueryAsync(ct);
            return captured;
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation
            or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.InsufficientPrivilege)
        { await Rollback(session, ct); return null; }
    }
    private static async Task Rollback(TenantDbSession session, CancellationToken ct)
    {
        await using var rollback = new NpgsqlCommand("ROLLBACK TO SAVEPOINT retention_source_write; RELEASE SAVEPOINT retention_source_write;", session.Connection, session.Transaction);
        await rollback.ExecuteNonQueryAsync(ct);
    }
}
