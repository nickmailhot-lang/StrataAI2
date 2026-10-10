using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Domain.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Application.Organizations;

public sealed record OrganizationConfigurationView(Guid OrganizationId, long Version,
    OrganizationConfigurationRecord? Revision);
public sealed record OrganizationConfigurationHistoryPage(Guid OrganizationId,
    IReadOnlyList<OrganizationConfigurationRecord> Items, long? NextBeforeVersion);

// API registration follows the Production store/migration. There is no Demo fallback
// for Production, and this command must never enter a nested Organization transaction.
public sealed class OrganizationConfigurationService(IOrganizationStore organizations,
    IOrganizationConfigurationStore configurations, IOrganizationUnitOfWork unit,
    ICommandActorAuthorization actors, IClock clock, IOrganizationConfigurationIntakeStore intake)
{
    public Task<OrganizationOperation<OrganizationConfigurationView>> ReadAsync(Guid organization,
        Guid actor, CancellationToken ct = default) => Owned(organization, actor, async () =>
        {
            var revision = await configurations.ReadAsync(organization, ct);
            return OrganizationOperation<OrganizationConfigurationView>.Success(new(organization, revision?.Version ?? 0, revision));
        }, ct);

    public Task<OrganizationOperation<OrganizationConfigurationHistoryPage>> ReadHistoryAsync(Guid organization,
        Guid actor, long? beforeVersion, CancellationToken ct = default) => Owned(organization, actor, async () =>
        {
            if (beforeVersion is <= 0)
                return OrganizationOperation<OrganizationConfigurationHistoryPage>.Failure("invalid_configuration_cursor");
            var rows = await configurations.ReadHistoryAsync(organization, beforeVersion, ct);
            var items = rows.Take(50).ToArray();
            return OrganizationOperation<OrganizationConfigurationHistoryPage>.Success(new(organization,
                items, rows.Count > 50 ? items[^1].Version : null));
        }, ct);

    public Task<OrganizationOperation<ConfigurationIntakeListPage>> ReadIntakeListsAsync(Guid organization,
        Guid actor, Guid board, string? afterRank, CancellationToken ct = default) => Owned(organization, actor, async () =>
        {
            // Cursor errors cannot disclose a guessed tenant to an unadmitted actor.
            if (afterRank is not null && !IntakeRank(afterRank))
                return OrganizationOperation<ConfigurationIntakeListPage>.Failure("invalid_configuration_intake_cursor");
            if (board == Guid.Empty)
                return OrganizationOperation<ConfigurationIntakeListPage>.Failure("configuration_intake_unavailable");
            var source = await intake.ReadListsAsync(organization, board, afterRank, ct);
            if (source is null)
                return OrganizationOperation<ConfigurationIntakeListPage>.Failure("configuration_intake_unavailable");
            if (source.Board.Id != board || source.Board.Version < 1 || string.IsNullOrWhiteSpace(source.Board.Name)
                || source.Board.Name.Length > 160 || source.Items.Count > 51)
                return OrganizationOperation<ConfigurationIntakeListPage>.Failure("configuration_source_unavailable");
            var previous = afterRank; var identities = new HashSet<Guid>();
            foreach (var row in source.Items)
            {
                if (row.Id == Guid.Empty || !identities.Add(row.Id) || row.Version < 1 || string.IsNullOrWhiteSpace(row.Name)
                    || row.Name.Length > 160 || !IntakeRank(row.Rank)
                    || previous is not null && string.CompareOrdinal(row.Rank, previous) <= 0)
                    return OrganizationOperation<ConfigurationIntakeListPage>.Failure("configuration_source_unavailable");
                previous = row.Rank;
            }
            var items = source.Items.Take(50).ToArray();
            return OrganizationOperation<ConfigurationIntakeListPage>.Success(new(organization, source.Board,
                items, source.Items.Count > 50 ? items[^1].Rank : null));
        }, ct);

    private static bool IntakeRank(string value) => value.Length == RankToken.Width && value.All(char.IsAsciiDigit);

    public Task<OrganizationOperation<OrganizationConfigurationRecord>> ChangeAsync(Guid organization, Guid actor,
        OrganizationConfigurationData? data, long expectedVersion, Guid key, string correlationId, CancellationToken ct = default)
    {
        // Freeze nested caller collections before awaiting a transaction lock. The
        // fingerprint and persisted revision describe the same reviewed intent.
        var frozen = data is null ? null : JsonSerializer.Deserialize<OrganizationConfigurationData>(JsonSerializer.Serialize(data));
        return Owned(organization, actor, async () =>
        {
            var field = OrganizationConfigurationRules.Validate(frozen);
            if (field is not null)
                return OrganizationOperation<OrganizationConfigurationRecord>.Failure("invalid_configuration_" + field);
            if (key == Guid.Empty)
                return OrganizationOperation<OrganizationConfigurationRecord>.Failure("idempotency_key_required");
            if (expectedVersion < 0 || expectedVersion == long.MaxValue)
                return OrganizationOperation<OrganizationConfigurationRecord>.Failure("version_conflict");
            if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 256 || correlationId.Any(char.IsControl))
                return OrganizationOperation<OrganizationConfigurationRecord>.Failure("invalid_correlation_id");
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                organization, expectedVersion, configuration = frozen,
            })));
            var receipt = await configurations.ReadReceiptAsync(organization, actor, key, ct);
            if (receipt is not null)
            {
                if (receipt.ExpiresAt <= clock.UtcNow)
                    return OrganizationOperation<OrganizationConfigurationRecord>.Failure("idempotency_key_expired");
                return receipt.Fingerprint == fingerprint
                    ? OrganizationOperation<OrganizationConfigurationRecord>.Success(receipt.Result)
                    : OrganizationOperation<OrganizationConfigurationRecord>.Failure("idempotency_key_conflict");
            }
            var current = await configurations.ReadAsync(organization, ct);
            if ((current?.Version ?? 0) != expectedVersion)
                return OrganizationOperation<OrganizationConfigurationRecord>.Failure("version_conflict");
            if (!await configurations.IntakeAvailableAsync(organization, frozen!.IntakeBoardId, frozen.IntakeListId, ct))
                return OrganizationOperation<OrganizationConfigurationRecord>.Failure("configuration_intake_unavailable");
            var parent = (await organizations.FindOrganizationAsync(organization, ct))!;
            var at = clock.UtcNow;
            if (at < parent.UpdatedAt || at < current?.UpdatedAt)
                return OrganizationOperation<OrganizationConfigurationRecord>.Failure("version_conflict");
            var revision = new OrganizationConfigurationRecord(organization, expectedVersion + 1, frozen!,
                parent.Name, parent.Type, parent.Version, actor, Guid.NewGuid(), correlationId, current?.CreatedAt ?? at, at);
            var result = await configurations.WriteAsync(expectedVersion, revision,
                new(actor, key, fingerprint, revision, at.AddHours(24)), ct);
            return result switch
            {
                OrganizationConfigurationWriteResult.Written => OrganizationOperation<OrganizationConfigurationRecord>.Success(revision),
                OrganizationConfigurationWriteResult.VersionConflict => OrganizationOperation<OrganizationConfigurationRecord>.Failure("version_conflict"),
                OrganizationConfigurationWriteResult.IdentifierConflict => OrganizationOperation<OrganizationConfigurationRecord>.Failure("configuration_identifier_unavailable"),
                OrganizationConfigurationWriteResult.KeyConflict => OrganizationOperation<OrganizationConfigurationRecord>.Failure("idempotency_key_conflict"),
                _ => OrganizationOperation<OrganizationConfigurationRecord>.Failure("configuration_source_unavailable"),
            };
        }, ct);
    }

    private Task<OrganizationOperation<T>> Owned<T>(Guid organization, Guid actor,
        Func<Task<OrganizationOperation<T>>> action, CancellationToken ct) => organization == Guid.Empty
        ? Task.FromResult(OrganizationOperation<T>.Failure("organization_not_found"))
        : unit.ExecuteAsync(organization, actor, null, false, async () =>
        {
            if (!await Admitted(organization, actor, ct))
                return OrganizationOperation<T>.Failure("organization_not_found");
            var result = await action();
            // Do not disclose private snapshots/receipts after current authority is lost.
            if (result.Succeeded && !await Admitted(organization, actor, ct))
                return OrganizationOperation<T>.Failure("organization_not_found");
            return result;
        }, ct);

    private async Task<bool> Admitted(Guid organization, Guid actor, CancellationToken ct) =>
        await actors.VerifyAsync(actor, ct)
        && (await organizations.FindOrganizationAsync(organization, ct))?.Status == OrganizationStatus.Active
        && await organizations.FindMembershipAsync(organization, actor, ct)
            is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin };

}
