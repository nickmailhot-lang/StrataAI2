using System.Text.Json;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Organizations;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationConfigurationStore(IOrganizationStore organizations,
    ICommandActorAuthorization actors, DemoWorkTransactionScope scope, IWorkManagementStore work)
    : IOrganizationConfigurationStore, IDemoOrganizationTransactionParticipant
{
    // Serialized snapshots prevent callers from mutating nested private collections in history.
    private readonly Dictionary<Guid, string> _current = [];
    private readonly Dictionary<(Guid Organization, long Version), string> _history = [];
    private readonly Dictionary<(Guid Organization, Guid Actor, Guid Key), string> _receipts = [];
    private readonly Dictionary<(Guid Organization, long Version), OrganizationConfigurationEvent> _events = [];
    private readonly HashSet<Guid> _eventIds = [];
    private readonly Dictionary<(string Jurisdiction, string Identifier), Guid> _identifiers = [];

    public async Task<bool> IntakeAvailableAsync(Guid organization, Guid? board, Guid? list, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (board is null) return list is null;
        if (board == Guid.Empty || list == Guid.Empty) return false;
        var selected = await work.FindBoardAsync(board.Value, ct);
        if (selected?.OrganizationId != organization || selected.LifecycleState != BoardLifecycleState.Active
            || !Enum.IsDefined(selected.Visibility)) return false;
        if (list is null) return true;
        var target = await work.FindListAsync(list.Value, ct);
        return target?.OrganizationId == organization && target.BoardId == board && target.LifecycleState == WorkItemLifecycleState.Active;
    }

    public Task<OrganizationConfigurationRecord?> ReadAsync(Guid organization, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return Task.FromResult(_current.TryGetValue(organization, out var json) ? Decode<OrganizationConfigurationRecord>(json) : null);
    }

    public Task<OrganizationConfigurationReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
    {
        RequireScope(organization, ct);
        return Task.FromResult(_receipts.TryGetValue((organization, actor, key), out var json) ? Decode<OrganizationConfigurationReceipt>(json) : null);
    }

    public Task<IReadOnlyList<OrganizationConfigurationRecord>> ReadHistoryAsync(Guid organization, long? beforeVersion, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (beforeVersion is <= 0) throw new ArgumentOutOfRangeException(nameof(beforeVersion));
        return Task.FromResult<IReadOnlyList<OrganizationConfigurationRecord>>(_history
            .Where(row => row.Key.Organization == organization && (!beforeVersion.HasValue || row.Key.Version < beforeVersion.Value))
            .OrderByDescending(row => row.Key.Version).Take(51).Select(row => Decode<OrganizationConfigurationRecord>(row.Value)).ToArray());
    }

    public Task<IReadOnlyList<OrganizationConfigurationEvent>> ReadEventsAsync(Guid organization, long afterVersion, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (afterVersion < 0) throw new ArgumentOutOfRangeException(nameof(afterVersion));
        return Task.FromResult<IReadOnlyList<OrganizationConfigurationEvent>>(_events
            .Where(row => row.Key.Organization == organization && row.Key.Version > afterVersion)
            .OrderBy(row => row.Key.Version).Take(51).Select(row => row.Value).ToArray());
    }

    public async Task<OrganizationConfigurationWriteResult> WriteAsync(long expectedVersion,
        OrganizationConfigurationRecord record, OrganizationConfigurationReceipt receipt, CancellationToken ct)
    {
        RequireScope(record.OrganizationId, ct);
        var current = await ReadAsync(record.OrganizationId, ct);
        if (expectedVersion < 0 || expectedVersion == long.MaxValue || (current?.Version ?? 0) != expectedVersion)
            return OrganizationConfigurationWriteResult.VersionConflict;
        if (_receipts.ContainsKey((record.OrganizationId, receipt.ActorId, receipt.Key)))
            return OrganizationConfigurationWriteResult.KeyConflict;
        var parent = await organizations.FindOrganizationAsync(record.OrganizationId, ct);
        var membership = await organizations.FindMembershipAsync(record.OrganizationId, record.ActorId, ct);
        if (!await actors.VerifyAsync(record.ActorId, ct) || parent?.Status != OrganizationStatus.Active
            || membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            || record.OrganizationName != parent.Name || record.OrganizationType != parent.Type || record.OrganizationVersion != parent.Version
            || record.Version != expectedVersion + 1 || record.ActorId == Guid.Empty || record.EventId == Guid.Empty || _eventIds.Contains(record.EventId)
            || string.IsNullOrWhiteSpace(record.CorrelationId) || record.CorrelationId.Length > 256
            || record.CorrelationId.Any(char.IsControl) || OrganizationConfigurationRules.Validate(record.Configuration) is not null
            || record.CreatedAt != (current?.CreatedAt ?? record.UpdatedAt) || record.UpdatedAt < (current?.UpdatedAt ?? parent.UpdatedAt)
            || record.UpdatedAt < record.CreatedAt || receipt.ActorId != record.ActorId || receipt.Key == Guid.Empty
            || receipt.ExpiresAt <= record.UpdatedAt || receipt.Fingerprint.Length != 64
            || !receipt.Fingerprint.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F')
            || JsonSerializer.Serialize(receipt.Result) != JsonSerializer.Serialize(record))
            return OrganizationConfigurationWriteResult.InvalidSource;
        var identifier = Identifier(record.Configuration);
        if (identifier is { } key && _identifiers.TryGetValue(key, out var owner) && owner != record.OrganizationId)
            return OrganizationConfigurationWriteResult.IdentifierConflict;
        var json = JsonSerializer.Serialize(record);
        var receiptJson = JsonSerializer.Serialize(receipt);
        ct.ThrowIfCancellationRequested();
        if (current is not null && Identifier(current.Configuration) is { } previous) _identifiers.Remove(previous);
        if (identifier is { } selected) _identifiers[selected] = record.OrganizationId;
        _current[record.OrganizationId] = json;
        _history.Add((record.OrganizationId, record.Version), json);
        _events.Add((record.OrganizationId, record.Version), new(record.EventId, record.OrganizationId,
            record.ActorId, record.Version, record.CorrelationId, record.UpdatedAt));
        _eventIds.Add(record.EventId);
        _receipts.Add((record.OrganizationId, receipt.ActorId, receipt.Key), receiptJson);
        return OrganizationConfigurationWriteResult.Written;
    }

    public Action CaptureRollback()
    {
        Action[] restore = [DemoRollback.Dictionary(_current), DemoRollback.Dictionary(_history),
            DemoRollback.Dictionary(_receipts), DemoRollback.Dictionary(_events), DemoRollback.Set(_eventIds), DemoRollback.Dictionary(_identifiers)];
        return () => { foreach (var action in restore) action(); };
    }

    private static T Decode<T>(string json) => JsonSerializer.Deserialize<T>(json)
        ?? throw new InvalidOperationException("Invalid private Organization configuration snapshot.");
    private static (string Jurisdiction, string Identifier)? Identifier(OrganizationConfigurationData data) =>
        data.CorporationIdentifier is null ? null : (data.Jurisdiction.Trim().ToUpperInvariant(), data.CorporationIdentifier.Trim().ToUpperInvariant());
    private void RequireScope(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !scope.OwnsOrganizationCommand(organization))
            throw new InvalidOperationException("Organization configuration requires an owning transaction.");
    }
}
