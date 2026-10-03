using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class PostgresAttachmentPreviewBackfillStore(PostgresConnectionFactory connections) : IAttachmentPreviewBackfillStore
{
    public async Task<AttachmentPreviewBackfillResult> EnqueuePageAsync(Guid organizationId, int maximumRows, CancellationToken ct)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization ID is required.", nameof(organizationId));
        if (maximumRows is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        ct.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await using var session = await connections.OpenTenantSessionAsync(organizationId, deadline.Token);
        // Bound DB lock waits and execution as well as the application deadline.
        await using (var settings = new NpgsqlCommand("SET LOCAL statement_timeout='1500ms'; SET LOCAL lock_timeout='250ms';",
            session.Connection, session.Transaction)) await settings.ExecuteNonQueryAsync(deadline.Token);
        await using var query = new NpgsqlCommand("SELECT visited,enqueued FROM public.enqueue_attachment_preview_backfill(@tenant,@limit);",
            session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("limit", maximumRows);
        AttachmentPreviewBackfillResult result;
        await using (var rows = await query.ExecuteReaderAsync(deadline.Token))
        {
            if (!await rows.ReadAsync(deadline.Token)) throw new InvalidOperationException("Attachment preview maintenance is unavailable.");
            result = new(rows.GetInt32(0), rows.GetInt32(1));
            if (result.Visited < 0 || result.Visited > maximumRows || result.Enqueued < 0 || result.Enqueued > result.Visited
                || await rows.ReadAsync(deadline.Token)) throw new InvalidOperationException("Attachment preview maintenance is unavailable.");
        }
        await session.CommitAsync(deadline.Token);
        return result;
    }
}
