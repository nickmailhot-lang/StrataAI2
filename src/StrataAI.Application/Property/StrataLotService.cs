using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Property;

namespace StrataAI.Application.Property;

// First delivery is an administrative directory. Internal membership alone and
// Portal grants do not authorize property directory disclosure or editing.
public sealed class StrataLotService(IOrganizationStore organizations, IOrganizationUnitOfWork transactions,
    IStrataLotStore lots, ICommandActorAuthorization actors, IClock clock)
{
    public Task<OrganizationOperation<Guid>> AdmitAsync(Guid organization, Guid actor, CancellationToken ct = default) =>
        Owned(organization, actor, () => Task.FromResult(OrganizationOperation<Guid>.Success(organization)), ct);

    public Task<OrganizationOperation<StrataLotPage>> ListAsync(Guid organization, Guid actor, Guid? after,
        CancellationToken ct = default) => Owned(organization, actor, async () => {
        if (after == Guid.Empty) return OrganizationOperation<StrataLotPage>.Failure("invalid_strata_lot_cursor");
        var rows = await lots.ListAsync(organization, after, ct);
        if (rows.Count > 51 || rows.Any(x => !Valid(x, organization)) || rows.Select(x => x.Id).Distinct().Count() != rows.Count
            || rows.Any(x => after is not null && x.Id.CompareTo(after.Value) <= 0)
            || !rows.Select(x => x.Id).SequenceEqual(rows.Select(x => x.Id).Order()))
            return OrganizationOperation<StrataLotPage>.Failure("strata_lot_source_unavailable");
        var items = rows.Take(50).ToArray();
        return OrganizationOperation<StrataLotPage>.Success(new(organization, items, rows.Count > 50 ? items[^1].Id : null));
    }, ct);

    public Task<OrganizationOperation<StrataLot>> ReadAsync(Guid organization, Guid actor, Guid id,
        CancellationToken ct = default) => Owned(organization, actor, async () => {
        var lot = await lots.ReadAsync(organization, id, ct);
        return lot is null ? OrganizationOperation<StrataLot>.Failure("strata_lot_not_found")
            : Valid(lot, organization) && lot.Id == id ? OrganizationOperation<StrataLot>.Success(lot)
            : OrganizationOperation<StrataLot>.Failure("strata_lot_source_unavailable");
    }, ct);

    public Task<OrganizationOperation<StrataLotHistoryPage>> HistoryAsync(Guid organization, Guid actor, Guid id,
        long? before, CancellationToken ct = default) => Owned(organization, actor, async () => {
        if (before is <= 0) return OrganizationOperation<StrataLotHistoryPage>.Failure("invalid_strata_lot_cursor");
        if (await lots.ReadAsync(organization, id, ct) is null)
            return OrganizationOperation<StrataLotHistoryPage>.Failure("strata_lot_not_found");
        var rows = await lots.HistoryAsync(organization, id, before, ct);
        if (rows.Count > 51 || rows.Any(x => !Valid(x.Lot, organization) || x.Lot.Id != id || x.ActorId == Guid.Empty || x.EventId == Guid.Empty))
            return OrganizationOperation<StrataLotHistoryPage>.Failure("strata_lot_source_unavailable");
        var items = rows.Take(50).ToArray();
        return OrganizationOperation<StrataLotHistoryPage>.Success(new(organization, id, items, rows.Count > 50 ? items[^1].Lot.Version : null));
    }, ct);

    public Task<OrganizationOperation<StrataLot>> ChangeAsync(Guid organization, Guid actor, Guid id, StrataLotData? data,
        long version, Guid key, string correlation, CancellationToken ct = default) => Owned(organization, actor, async () => {
        var invalid = StrataLotRules.Validate(data);
        if (invalid is not null || id == Guid.Empty)
            return OrganizationOperation<StrataLot>.Failure("invalid_strata_lot_" + (invalid ?? "id"));
        if (key == Guid.Empty) return OrganizationOperation<StrataLot>.Failure("idempotency_key_required");
        if (version < 0 || version == long.MaxValue) return OrganizationOperation<StrataLot>.Failure("version_conflict");
        if (!StrataLotRules.Text(correlation, 256, true)) return OrganizationOperation<StrataLot>.Failure("invalid_strata_lot_correlation");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { organization, id, version, data })));
        var receipt = await lots.ReceiptAsync(organization, actor, key, ct);
        if (receipt is not null) {
            if (receipt.ActorId != actor || receipt.Key != key || !Valid(receipt.Result, organization))
                return OrganizationOperation<StrataLot>.Failure("strata_lot_source_unavailable");
            if (receipt.ExpiresAt <= clock.UtcNow) return OrganizationOperation<StrataLot>.Failure("idempotency_key_expired");
            if (receipt.Fingerprint != fingerprint) return OrganizationOperation<StrataLot>.Failure("idempotency_key_conflict");
            if (receipt.Result.Id != id || receipt.Result.Version != version + 1 || receipt.Result.Data != data)
                return OrganizationOperation<StrataLot>.Failure("strata_lot_source_unavailable");
            return OrganizationOperation<StrataLot>.Success(receipt.Result);
        }
        var current = await lots.ReadAsync(organization, id, ct);
        if (current is not null && (!Valid(current, organization) || current.Id != id))
            return OrganizationOperation<StrataLot>.Failure("strata_lot_source_unavailable");
        if ((current?.Version ?? 0) != version) return OrganizationOperation<StrataLot>.Failure("version_conflict");
        var now = clock.UtcNow;
        if (now < current?.UpdatedAt || now > DateTimeOffset.MaxValue.AddHours(-24))
            return OrganizationOperation<StrataLot>.Failure("version_conflict");
        var updated = new StrataLot(id, organization, version + 1, data!, current?.CreatedAt ?? now, now);
        var revision = new StrataLotRevision(updated, actor, Guid.NewGuid(), correlation);
        var result = await lots.WriteAsync(revision, new(actor, key, fingerprint, updated, now.AddHours(24)), version, ct);
        if (result != StrataLotWriteResult.Written) return OrganizationOperation<StrataLot>.Failure(
            result == StrataLotWriteResult.IdentifierConflict ? "strata_lot_identifier_unavailable" : "version_conflict");
        await organizations.AppendAuditAsync(organization, actor, current is null ? "STRATA_LOT_CREATED" : "STRATA_LOT_UPDATED",
            "StrataLot", id, correlation, ct);
        return OrganizationOperation<StrataLot>.Success(updated);
    }, ct);

    private static bool Valid(StrataLot? lot, Guid organization) => lot is not null && lot.OrganizationId == organization && lot.Id != Guid.Empty
        && lot.Version > 0 && lot.UpdatedAt >= lot.CreatedAt && StrataLotRules.Validate(lot.Data) is null;
    private Task<OrganizationOperation<T>> Owned<T>(Guid organization, Guid actor, Func<Task<OrganizationOperation<T>>> action,
        CancellationToken ct) => organization == Guid.Empty ? Task.FromResult(OrganizationOperation<T>.Failure("organization_not_found"))
        : transactions.ExecuteAsync(organization, actor, null, false, async () => {
            if (!await Admitted(organization, actor, ct)) return OrganizationOperation<T>.Failure("organization_not_found");
            var result = await action();
            return result.Succeeded && !await Admitted(organization, actor, ct)
                ? OrganizationOperation<T>.Failure("organization_not_found") : result;
        }, ct);
    private async Task<bool> Admitted(Guid organization, Guid actor, CancellationToken ct) => await actors.VerifyAsync(actor, ct)
        && (await organizations.FindOrganizationAsync(organization, ct))?.Status == OrganizationStatus.Active
        && await organizations.FindMembershipAsync(organization, actor, ct) is { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin };
}
