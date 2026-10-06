using System.Text.Json;
using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public sealed class PostgresOrganizationDeletionCandidateReader(PostgresConnectionFactory connections) : IOrganizationDeletionCandidateReader
{
    public async Task<OrganizationDeletionCandidatePage?> ReadAsync(ClaimedBackgroundJob job, OrganizationDeletionAttempt attempt,
        int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > OrganizationDeletionJobs.PageSize || job.OrganizationId == Guid.Empty
            || job.JobType != OrganizationDeletionJobs.Type || job.ServiceIdentity != OrganizationDeletionJobs.Service
            || OrganizationDeletionAttempt.Parse(job.SafeMetadataJson) != attempt) return null;
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand("SELECT public.load_organization_deletion_page(@tenant,@job,@actor,@worker,@lease,@request,@step,@version,@limit)", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", job.OrganizationId); command.Parameters.AddWithValue("job", job.Id);
        command.Parameters.AddWithValue("actor", job.ActorId); command.Parameters.AddWithValue("worker", job.WorkerId);
        command.Parameters.AddWithValue("lease", job.LeaseId); command.Parameters.AddWithValue("request", attempt.RequestId);
        command.Parameters.AddWithValue("step", attempt.StepId); command.Parameters.AddWithValue("version", attempt.AcceptedVersion);
        command.Parameters.AddWithValue("limit", pageSize);
        if (await command.ExecuteScalarAsync(cancellationToken) is not string json) return null;
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Guid? Optional(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();
        var phase = root.GetProperty("phase").GetString() switch {
            "ATTACHMENTS" => OrganizationDeletionPhase.Attachments, "CARDS" => OrganizationDeletionPhase.Cards,
            "LISTS" => OrganizationDeletionPhase.Lists, "BOARDS" => OrganizationDeletionPhase.Boards,
            "FINALIZE" => OrganizationDeletionPhase.Finalize, _ => throw new InvalidOperationException("Deletion checkpoint is unavailable.") };
        var items = root.GetProperty("items").EnumerateArray().Select(item => new OrganizationDeletionCandidate(
            item.GetProperty("id").GetGuid(), item.GetProperty("boardId").GetGuid(), Optional(item.GetProperty("cardId")),
            item.GetProperty("version").GetInt64(), item.GetProperty("state").GetString()!)).ToArray();
        return new(phase, Optional(root.GetProperty("afterId")), root.GetProperty("version").GetInt64(), items, Optional(root.GetProperty("nextCursor")));
    }
}
