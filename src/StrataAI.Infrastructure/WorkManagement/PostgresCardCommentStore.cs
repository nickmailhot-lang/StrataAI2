using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class PostgresCardCommentStore(PostgresConnectionFactory connections) : ICardCommentStore
{
    private const string Fields = "id,tenant_id,card_id,author_id,content,created_at,updated_at,version,edited_at,deleted_at,deleted_by";
    private void RequireScope(Guid organization)
    {
        if (!connections.HasCommandScope(organization)) throw new InvalidOperationException("Comment storage requires the owning scope.");
    }
    public async Task<CardCommentRecord> CreateAsync(Guid id, Guid organization, Guid card, Guid author, string content, DateTimeOffset at, CancellationToken ct)
    {
        RequireScope(organization); var comment = new CardComment(id, organization, card, author, content, AttachmentMetadataMapping.DatabaseTimestamp(at));
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at) VALUES(@id,@tenant,@card,@author,@content,@at,@at) RETURNING {Fields};", session.Connection, session.Transaction);
        Scope(query, organization, card); query.Parameters.AddWithValue("id", comment.Id); query.Parameters.AddWithValue("author", comment.AuthorId);
        query.Parameters.AddWithValue("content", comment.Content!); query.Parameters.AddWithValue("at", comment.CreatedAt);
        await using var reader = await query.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Comment insert is unavailable.");
        return Read(reader);
    }
    public async Task<CardCommentRecord?> FindAsync(Guid organization, Guid card, Guid comment, CancellationToken ct)
    {
        RequireScope(organization); await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"SELECT {Fields} FROM card_comments WHERE tenant_id=@tenant AND card_id=@card AND id=@id;", session.Connection, session.Transaction);
        Scope(query, organization, card); query.Parameters.AddWithValue("id", comment);
        await using var reader = await query.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<IReadOnlyList<CardCommentRecord>> ListAsync(Guid organization, Guid card, DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct)
    {
        RequireScope(organization);
        if (beforeCreatedAt.HasValue != beforeId.HasValue || beforeId == Guid.Empty) throw new ArgumentException("Comment cursor is invalid.");
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"SELECT {Fields} FROM card_comments WHERE tenant_id=@tenant AND card_id=@card AND (@at IS NULL OR (created_at,id)<(@at,@id)) ORDER BY created_at DESC,id DESC LIMIT 51;", session.Connection, session.Transaction);
        Scope(query, organization, card); query.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, (object?)beforeCreatedAt?.ToUniversalTime() ?? DBNull.Value);
        query.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, (object?)beforeId ?? DBNull.Value);
        var rows = new List<CardCommentRecord>(); await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(Read(reader)); return rows;
    }
    public Task<CardCommentRecord?> EditAsync(Guid organization, Guid card, Guid comment, Guid author, long version, string content, DateTimeOffset at, CancellationToken ct)
        => Change(organization, card, comment, author, version, CardComment.RequireContent(content), at, false, ct);
    public Task<CardCommentRecord?> DeleteAsync(Guid organization, Guid card, Guid comment, Guid author, long version, DateTimeOffset at, CancellationToken ct)
        => Change(organization, card, comment, author, version, null, at, true, ct);
    private async Task<CardCommentRecord?> Change(Guid organization, Guid card, Guid comment, Guid author, long version, string? content, DateTimeOffset at, bool deleting, CancellationToken ct)
    {
        RequireScope(organization); await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        var transition = deleting ? "content=NULL,deleted_at=@at,deleted_by=@author" : "content=@content,edited_at=@at";
        var changed = deleting ? "" : " AND content IS DISTINCT FROM @content";
        await using var query = new NpgsqlCommand($"UPDATE card_comments SET {transition},updated_at=@at,version=version+1 WHERE tenant_id=@tenant AND card_id=@card AND id=@id AND author_id=@author AND version=@version AND version<9223372036854775807 AND updated_at<=@at AND deleted_at IS NULL{changed} RETURNING {Fields};", session.Connection, session.Transaction);
        Scope(query, organization, card); query.Parameters.AddWithValue("id", comment); query.Parameters.AddWithValue("author", author);
        query.Parameters.AddWithValue("version", version); query.Parameters.AddWithValue("at", AttachmentMetadataMapping.DatabaseTimestamp(at));
        if (!deleting) query.Parameters.AddWithValue("content", content!);
        await using var reader = await query.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    private static void Scope(NpgsqlCommand query, Guid organization, Guid card)
    { query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card); }
    private static CardCommentRecord Read(NpgsqlDataReader row) => new(row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetGuid(3),
        row.IsDBNull(4) ? null : row.GetString(4), row.GetFieldValue<DateTimeOffset>(5), row.GetFieldValue<DateTimeOffset>(6), row.GetInt64(7),
        row.IsDBNull(8) ? null : row.GetFieldValue<DateTimeOffset>(8), row.IsDBNull(9) ? null : row.GetFieldValue<DateTimeOffset>(9), row.IsDBNull(10) ? null : row.GetGuid(10));
}
