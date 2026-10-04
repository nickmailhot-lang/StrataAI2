using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<BoardBackgroundImage?> FindBoardBackgroundImageAsync(Guid organization, Guid board, Guid image, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Board image metadata requires its owning read or command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("""
            SELECT b.id,b.created_by,b.created_at,b.source_image_id,m.id,m.output_size_bytes,m.output_sha256,m.width,m.height
            FROM board_background_images b JOIN attachment_previews m ON m.id=b.preview_id AND m.tenant_id=b.tenant_id
            JOIN attachment_preview_publications p ON p.id=m.id AND p.tenant_id=m.tenant_id
            WHERE b.tenant_id=@tenant AND b.board_id=@board AND b.id=@image;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("board", board); query.Parameters.AddWithValue("image", image);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), organization, board, reader.GetGuid(1),
            reader.GetFieldValue<DateTimeOffset>(2), new(new(AttachmentObjectReference.ForPreview(organization, reader.GetGuid(4)),
                reader.GetInt64(5), reader.GetString(6)), reader.GetInt32(7), reader.GetInt32(8)), reader.IsDBNull(3) ? null : reader.GetGuid(3)) : null;
    }

    public async Task<BoardBackgroundImage> CreateBoardBackgroundImageAsync(BoardBackgroundImage image, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(image.OrganizationId) || !image.Preview.Integrity.Reference.IsPreview
            || image.Preview.Integrity.Reference.OrganizationId != image.OrganizationId)
            throw new InvalidOperationException("Board image creation requires its owning tenant command and published preview.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(image.OrganizationId, ct);
        await using var query = new NpgsqlCommand("""
            INSERT INTO board_background_images(id,tenant_id,board_id,preview_id,created_by,created_at,source_image_id)
            SELECT @id,@tenant,@board,m.id,@actor,@now,@source FROM attachment_previews m
            JOIN attachment_preview_publications p ON p.id=m.id AND p.tenant_id=m.tenant_id
            WHERE m.tenant_id=@tenant AND m.id=@preview AND m.output_size_bytes=@size AND m.output_sha256=@sha
              AND m.width=@width AND m.height=@height RETURNING id;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("id", image.Id); query.Parameters.AddWithValue("tenant", image.OrganizationId);
        query.Parameters.AddWithValue("board", image.BoardId); query.Parameters.AddWithValue("actor", image.CreatedBy);
        query.Parameters.AddWithValue("now", image.CreatedAt); query.Parameters.AddWithValue("source", NpgsqlDbType.Uuid, (object?)image.SourceImageId ?? DBNull.Value);
        query.Parameters.AddWithValue("preview", image.Preview.Integrity.Reference.AttachmentId);
        query.Parameters.AddWithValue("size", image.Preview.Integrity.SizeBytes); query.Parameters.AddWithValue("sha", image.Preview.Integrity.Sha256);
        query.Parameters.AddWithValue("width", image.Preview.Width); query.Parameters.AddWithValue("height", image.Preview.Height);
        if (await query.ExecuteScalarAsync(ct) is not Guid) throw new InvalidOperationException("Published Board image is unavailable.");
        await session.CommitAsync(ct);
        return image with { CreatedAt = AttachmentMetadataMapping.DatabaseTimestamp(image.CreatedAt) };
    }

    public async Task<BoardRecord?> InitializeCopiedBoardBackgroundAsync(Guid organization, Guid board, Guid image, DateTimeOffset createdAt, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Board copy image initialization requires its owning command.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("""
            UPDATE boards SET background_type='IMAGE',background_value=@image_text
            WHERE tenant_id=@tenant AND id=@board AND version=1 AND created_at=@now AND updated_at=@now
              AND background_type='COLOR' AND background_value IS NULL AND lifecycle_state='ACTIVE'
              AND EXISTS(SELECT 1 FROM board_background_images i WHERE i.id=@image AND i.tenant_id=@tenant AND i.board_id=@board
                AND i.source_image_id IS NOT NULL AND i.created_at=@now) RETURNING id;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("board", board);
        query.Parameters.AddWithValue("image", image); query.Parameters.AddWithValue("image_text", image.ToString("D")); query.Parameters.AddWithValue("now", createdAt);
        var found = await query.ExecuteScalarAsync(ct) is Guid; await session.CommitAsync(ct);
        return found ? await FindBoardAsync(board, ct) : null;
    }

    public async Task<bool> AcquirePublicBoardBackgroundReadScopeAsync(Guid organization, Guid board, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Public Board image reads require their owning read transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using (var parent = new NpgsqlCommand("SELECT id FROM organizations WHERE id=@tenant AND status='ACTIVE' FOR SHARE;", session.Connection, session.Transaction))
        { parent.Parameters.AddWithValue("tenant", organization); if (await parent.ExecuteScalarAsync(ct) is not Guid) return false; }
        await using var query = new NpgsqlCommand("SELECT id FROM boards WHERE tenant_id=@tenant AND id=@board AND visibility='PUBLIC' AND lifecycle_state='ACTIVE' FOR SHARE;", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("board", board);
        return await query.ExecuteScalarAsync(ct) is Guid;
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    private readonly Dictionary<Guid, BoardBackgroundImage> _boardBackgroundImages = [];
    public Task<BoardBackgroundImage?> FindBoardBackgroundImageAsync(Guid organization, Guid board, Guid image, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock (_sync)
            return Task.FromResult(_boardBackgroundImages.TryGetValue(image, out var row) && row.OrganizationId == organization && row.BoardId == board ? row : null);
    }
    public Task<BoardBackgroundImage> CreateBoardBackgroundImageAsync(BoardBackgroundImage image, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock (_sync)
        {
            if (!transactionScope.Owns(image.OrganizationId) || !_boards.TryGetValue(image.BoardId, out var board) || board.OrganizationId != image.OrganizationId
                || image.Id == Guid.Empty || image.CreatedBy == Guid.Empty || !image.Preview.Integrity.Reference.IsPreview
                || image.Preview.Integrity.Reference.OrganizationId != image.OrganizationId
                || image.Preview.Integrity.SizeBytes is < 45 or > 8388608 || image.Preview.Width is < 1 or > 1024 || image.Preview.Height is < 1 or > 1024
                || image.SourceImageId is { } source && (!_boardBackgroundImages.TryGetValue(source, out var original)
                    || original.OrganizationId != image.OrganizationId || original.BoardId == image.BoardId || original.Preview != image.Preview || original.CreatedAt > image.CreatedAt))
                throw new InvalidOperationException("Board image scope is invalid.");
            _boardBackgroundImages.Add(image.Id, image); return Task.FromResult(image);
        }
    }
    public Task<BoardRecord?> InitializeCopiedBoardBackgroundAsync(Guid organization, Guid board, Guid image, DateTimeOffset createdAt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock (_sync)
        {
            if (!transactionScope.Owns(organization)) throw new InvalidOperationException("Board copy image initialization requires its owning command.");
            if (!_boards.TryGetValue(board, out var current) || current.OrganizationId != organization || current.Version != 1
                || current.CreatedAt != createdAt || current.UpdatedAt != createdAt || current.BackgroundType != "COLOR" || current.BackgroundValue is not null
                || current.LifecycleState != BoardLifecycleState.Active
                || !_boardBackgroundImages.TryGetValue(image, out var owned) || owned.OrganizationId != organization || owned.BoardId != board
                || owned.SourceImageId is null || owned.CreatedAt != createdAt) return Task.FromResult<BoardRecord?>(null);
            var result = current with { BackgroundType = "IMAGE", BackgroundValue = image.ToString("D") };
            _boards[board] = result; return Task.FromResult<BoardRecord?>(result);
        }
    }
    public Task<bool> AcquirePublicBoardBackgroundReadScopeAsync(Guid organization, Guid board, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock (_sync) return Task.FromResult(transactionScope.Owns(organization)
            && _boards.TryGetValue(board, out var row) && row.OrganizationId == organization && row.Visibility == BoardVisibility.Public && row.LifecycleState == BoardLifecycleState.Active);
    }
}
