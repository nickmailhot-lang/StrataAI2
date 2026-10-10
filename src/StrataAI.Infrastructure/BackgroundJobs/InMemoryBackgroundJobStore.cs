using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.BackgroundJobs;

// Process-local Demo publication and leases. Consumers use the same Work gate
// as command rollback, so neither can restore over the other's committed work.
internal sealed class InMemoryBackgroundJobStore(IClock clock, DemoWorkTransactionScope scope,
    InMemoryAccountOrganizationGate gate) : IBackgroundJobStore, IDemoWorkTransactionParticipant
{
    private sealed record Entry(NewBackgroundJob Job, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset AvailableAt,
        long Version = 1,
        string State = "PENDING", int Attempts = 0, Guid? Lease = null, Guid? Worker = null,
        DateTimeOffset? LeaseExpiresAt = null);
    private readonly Dictionary<(Guid Organization, string Type, string Key), Entry> _rows = new();

    // Only admitted producers in the owning command can publish. A duplicate
    // retains its first payload, schedule and identity, even after completion.
    internal void Publish(NewBackgroundJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!scope.Owns(job.OrganizationId))
            throw new InvalidOperationException("Demo job publication requires its owning Organization command.");
        if (job.Id == Guid.Empty || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty
            || !Regex.IsMatch(job.JobType, "\\A[A-Z][A-Z0-9_]{0,79}\\z", RegexOptions.NonBacktracking)
            || job.IdempotencyKey.Length is < 1 or > 200
            || string.IsNullOrWhiteSpace(job.ServiceIdentity) || job.ServiceIdentity.Length > 120
            || string.IsNullOrWhiteSpace(job.CorrelationId) || job.CorrelationId.Length > 120)
            throw new InvalidOperationException("Invalid Demo job references.");
        using var metadata = JsonDocument.Parse(job.SafeMetadataJson);
        if (metadata.RootElement.ValueKind != JsonValueKind.Object
            || System.Text.Encoding.UTF8.GetByteCount(job.SafeMetadataJson) > 32768)
            throw new InvalidOperationException("Invalid Demo job metadata.");
        var key = (job.OrganizationId, job.JobType, job.IdempotencyKey);
        if (_rows.ContainsKey(key)) return;
        if (_rows.Count >= 10000 || _rows.Values.Any(row => row.Job.Id == job.Id))
            throw new InvalidOperationException("Demo job publication is unavailable.");
        var now = clock.UtcNow;
        _rows.Add(key, new(job, now, now, job.AvailableAt ?? now));
    }

    public Action CaptureRollback()
    {
        var snapshot = _rows.ToArray();
        return () => { _rows.Clear(); foreach (var row in snapshot) _rows.Add(row.Key, row.Value); };
    }

    public Task<ClaimedBackgroundJob?> ClaimAsync(Guid organizationId, Guid workerId, CancellationToken cancellationToken = default) =>
        ClaimCoreAsync(organizationId, workerId, null, cancellationToken);

    internal Task<ClaimedBackgroundJob?> ClaimReminderAsync(Guid organizationId, Guid workerId, CancellationToken ct) =>
        ClaimCoreAsync(organizationId, workerId, StrataAI.Application.WorkManagement.CardReminderDeliveryHandler.Type, ct);

    internal async Task<Guid?> NextDueReminderOrganizationAsync(Guid? after, CancellationToken ct)
    {
        await gate.WorkCommands.WaitAsync(ct);
        try
        {
            var now = clock.UtcNow;
            var organizations = _rows.Values.Where(r => r.Job.JobType == StrataAI.Application.WorkManagement.CardReminderDeliveryHandler.Type &&
                (r.State == "PENDING" && r.Attempts < 5 && r.AvailableAt <= now ||
                 r.State == "RUNNING" && r.LeaseExpiresAt <= now))
                .Select(r => r.Job.OrganizationId).Distinct().Order().ToArray();
            if (organizations.Length == 0) return null;
            return after is null ? organizations[0] : organizations.FirstOrDefault(id => id.CompareTo(after.Value) > 0, organizations[0]);
        }
        finally { gate.WorkCommands.Release(); }
    }

    private async Task<ClaimedBackgroundJob?> ClaimCoreAsync(Guid organizationId, Guid workerId, string? jobType, CancellationToken cancellationToken)
    {
        ValidateScope(organizationId, workerId);
        await gate.WorkCommands.WaitAsync(cancellationToken);
        try
        {
            var now = clock.UtcNow;
            foreach (var row in _rows.Where(row => row.Key.Organization == organizationId && (jobType is null || row.Value.Job.JobType == jobType)
                && row.Value.State == "RUNNING" && row.Value.LeaseExpiresAt <= now && row.Value.Attempts >= 5).ToArray())
                _rows[row.Key] = row.Value with { State = "FAILED", UpdatedAt = now, Version = row.Value.Version + 1,
                    Lease = null, Worker = null, LeaseExpiresAt = null };
            var candidate = _rows.Where(row => row.Key.Organization == organizationId && (jobType is null || row.Value.Job.JobType == jobType) && row.Value.Attempts < 5
                && (row.Value.State == "PENDING" && row.Value.AvailableAt <= now
                    || row.Value.State == "RUNNING" && row.Value.LeaseExpiresAt <= now))
                .OrderBy(row => row.Value.AvailableAt).ThenBy(row => row.Value.CreatedAt).ThenBy(row => row.Value.Job.Id)
                .FirstOrDefault();
            if (candidate.Value is not { } pending) return null;
            var claimed = pending with { State = "RUNNING", UpdatedAt = now, Version = pending.Version + 1,
                Attempts = pending.Attempts + 1,
                Lease = Guid.NewGuid(), Worker = workerId, LeaseExpiresAt = now.AddMinutes(2) };
            _rows[candidate.Key] = claimed;
            var job = claimed.Job;
            return new(job.Id, job.OrganizationId, job.JobType, job.ActorId, job.ServiceIdentity,
                job.CorrelationId, job.SafeMetadataJson, claimed.Attempts, claimed.Lease!.Value,
                workerId, claimed.LeaseExpiresAt!.Value);
        }
        finally { gate.WorkCommands.Release(); }
    }

    public Task<bool> CompleteAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, CancellationToken cancellationToken = default) =>
        FinishAsync(organizationId, jobId, leaseId, workerId, null, cancellationToken);

    public Task<bool> FailAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, string errorCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(errorCode) || !Regex.IsMatch(errorCode, "\\A[a-z][a-z0-9_]{0,79}\\z", RegexOptions.NonBacktracking))
            throw new ArgumentException("Safe stable error code is required.", nameof(errorCode));
        return FinishAsync(organizationId, jobId, leaseId, workerId, errorCode, cancellationToken);
    }

    private async Task<bool> FinishAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, string? errorCode, CancellationToken ct)
    {
        ValidateScope(organizationId, workerId);
        await gate.WorkCommands.WaitAsync(ct);
        try
        {
            var entry = _rows.FirstOrDefault(row => row.Key.Organization == organizationId && row.Value.Job.Id == jobId);
            var row = entry.Value;
            var now = clock.UtcNow;
            if (row is null || row.State != "RUNNING" || row.Lease != leaseId || row.Worker != workerId || row.LeaseExpiresAt <= now)
                return false;
            _rows[entry.Key] = row with { State = errorCode is null ? "SUCCEEDED" : row.Attempts >= 5 ? "FAILED" : "PENDING",
                UpdatedAt = now, Version = row.Version + 1,
                AvailableAt = errorCode is null ? row.AvailableAt : now.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, row.Attempts - 1))),
                Lease = null, Worker = null, LeaseExpiresAt = null };
            return true;
        }
        finally { gate.WorkCommands.Release(); }
    }

    // Caller holds the same Work gate as claim/finish and owning rollback.
    internal bool HasLiveClaim(ClaimedBackgroundJob claim, string expectedKey)
    {
        if (!scope.Owns(claim.OrganizationId)) throw new InvalidOperationException("Demo lease inspection requires its owning transaction.");
        var row = _rows.Values.SingleOrDefault(r => r.Job.Id == claim.Id && r.Job.OrganizationId == claim.OrganizationId);
        return row is not null && row.State == "RUNNING" && row.Lease == claim.LeaseId && row.Worker == claim.WorkerId
            && row.LeaseExpiresAt > clock.UtcNow && row.LeaseExpiresAt == claim.LeaseExpiresAt
            && row.Attempts == claim.AttemptCount && row.Job.ActorId == claim.ActorId
            && row.Job.JobType == claim.JobType && row.Job.ServiceIdentity == claim.ServiceIdentity
            && row.Job.CorrelationId == claim.CorrelationId && row.Job.SafeMetadataJson == claim.SafeMetadataJson
            && row.Job.IdempotencyKey == expectedKey;
    }

    private static void ValidateScope(Guid organization, Guid worker)
    {
        if (organization == Guid.Empty || worker == Guid.Empty)
            throw new ArgumentException("Organization and Worker identities are required.");
    }
}

internal static class DemoBackgroundJobRegistration
{
    internal static void AddDemoBackgroundJobs(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<InMemoryAccountOrganizationGate>();
        services.TryAddSingleton<DemoWorkTransactionScope>();
        services.TryAddSingleton<InMemoryBackgroundJobStore>();
        services.TryAddSingleton<IBackgroundJobStore>(provider => provider.GetRequiredService<InMemoryBackgroundJobStore>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDemoWorkTransactionParticipant, InMemoryBackgroundJobParticipant>());
    }

    private sealed class InMemoryBackgroundJobParticipant(InMemoryBackgroundJobStore store) : IDemoWorkTransactionParticipant
    {
        public Action CaptureRollback() => store.CaptureRollback();
    }
}
