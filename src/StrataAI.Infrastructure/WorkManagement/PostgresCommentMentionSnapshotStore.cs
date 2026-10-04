using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresCommentMentionSnapshotStore(PostgresConnectionFactory connections) : ICommentMentionSnapshotStore
{
    private void RequireScope(Guid organization)
    {
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Mention snapshots require the owning Work transaction.");
    }
    public async Task<CommentMentionSnapshot?> FindSnapshotAsync(Guid organization, Guid card, Guid comment, long version, CancellationToken ct = default)
    {
        RequireScope(organization); CommentMentionSnapshot.RequireIdentity(organization, card, comment, version);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("""
            SELECT s.created_at,s.recipient_count,ARRAY(
              SELECT r.recipient_id FROM comment_mention_recipients r
              WHERE r.tenant_id=s.tenant_id AND r.card_id=s.card_id AND r.comment_id=s.comment_id AND r.comment_version=s.comment_version
              ORDER BY r.recipient_id)
            FROM comment_mention_snapshots s
            WHERE s.tenant_id=@tenant AND s.card_id=@card AND s.comment_id=@comment AND s.comment_version=@version;
            """, session.Connection, session.Transaction);
        Bind(query, organization, card, comment, version);
        await using var rows = await query.ExecuteReaderAsync(ct);
        if (!await rows.ReadAsync(ct)) return null;
        var recipients = rows.GetFieldValue<Guid[]>(2);
        if (recipients.Length != rows.GetInt32(1)) throw new InvalidOperationException("Mention snapshot is incomplete.");
        return new(organization, card, comment, version, rows.GetFieldValue<DateTimeOffset>(0), recipients);
    }
    public async Task AppendSnapshotAsync(CommentMentionSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); RequireScope(snapshot.OrganizationId); ct.ThrowIfCancellationRequested();
        var existing = await FindSnapshotAsync(snapshot.OrganizationId, snapshot.CardId, snapshot.CommentId, snapshot.CommentVersion, ct);
        if (existing is not null) { Match(existing, snapshot); return; }
        await using var session = await connections.OpenTenantSessionAsync(snapshot.OrganizationId, ct);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO comment_mention_snapshots(tenant_id,card_id,comment_id,comment_version,created_at,recipient_count)
            VALUES(@tenant,@card,@comment,@version,@at,@count)
            ON CONFLICT(tenant_id,comment_id,comment_version) DO NOTHING RETURNING true;
            """, session.Connection, session.Transaction);
        Bind(insert, snapshot.OrganizationId, snapshot.CardId, snapshot.CommentId, snapshot.CommentVersion);
        insert.Parameters.AddWithValue("at", snapshot.CreatedAt); insert.Parameters.AddWithValue("count", snapshot.Recipients.Count);
        if (await insert.ExecuteScalarAsync(ct) is not true)
        {
            existing = await FindSnapshotAsync(snapshot.OrganizationId, snapshot.CardId, snapshot.CommentId, snapshot.CommentVersion, ct);
            if (existing is null) throw new InvalidOperationException("Mention snapshot is unavailable.");
            Match(existing, snapshot); return;
        }
        if (snapshot.Recipients.Count == 0) return;
        await using var recipients = new NpgsqlCommand("""
            INSERT INTO comment_mention_recipients(tenant_id,card_id,comment_id,comment_version,recipient_id)
            SELECT @tenant,@card,@comment,@version,id FROM unnest(@recipients) AS targets(id);
            """, session.Connection, session.Transaction);
        Bind(recipients, snapshot.OrganizationId, snapshot.CardId, snapshot.CommentId, snapshot.CommentVersion);
        recipients.Parameters.AddWithValue("recipients", snapshot.Recipients.ToArray()); await recipients.ExecuteNonQueryAsync(ct);
    }
    private static void Match(CommentMentionSnapshot existing, CommentMentionSnapshot proposed)
    { if (!existing.SameAs(proposed)) throw new InvalidOperationException("Mention snapshot revision was reused."); }
    private static void Bind(NpgsqlCommand query, Guid organization, Guid card, Guid comment, long version)
    {
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card);
        query.Parameters.AddWithValue("comment", comment); query.Parameters.AddWithValue("version", version);
    }
}
