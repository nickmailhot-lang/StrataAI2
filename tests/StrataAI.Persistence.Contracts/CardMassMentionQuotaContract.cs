using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

internal static class CardMassMentionQuotaContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid card, Guid actor,
        Guid foreignTenant, Guid foreignCard, Guid foreignActor, CancellationToken ct)
    {
        var quota = provider.GetRequiredService<ICardMassMentionQuota>(); var events = provider.GetRequiredService<IWorkEventStore>();
        var work = provider.GetRequiredService<IWorkManagementStore>(); var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        var eventIds = new List<Guid>();
        async Task<WorkEvent> Source(Guid organization, Guid parentId, Guid author)
        {
            var parent = await work.FindCardAsync(parentId, ct) ?? throw new InvalidOperationException("Quota fixture Card is unavailable.");
            return new(Guid.NewGuid(), organization, parent.BoardId, author, "MENTION_CREATED", "Card", parentId,
                parent.Version, "mass-quota-contract", AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow));
        }
        async Task<WorkOperation<bool>> Reserve(WorkEvent source, bool refuse = false, bool append = true)
            => await unit.ExecuteReadAsync(source.OrganizationId, null, "fixture_denied", () => Task.FromResult(true), async () =>
            {
                if (append) await events.AppendAsync(source, ct);
                if (!await quota.TryReserveAsync(source, ct)) return WorkOperation<bool>.Failure("mass_mention_rate_limited");
                return refuse ? WorkOperation<bool>.Failure("fixture_refused") : WorkOperation<bool>.Success(true);
            }, ct);
        async Task<long> Count(Guid organization)
        {
            await using var query = new NpgsqlCommand("SELECT count(*) FROM mass_mention_reservations WHERE tenant_id=@tenant AND event_id=ANY(@events);", admin);
            query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("events", eventIds.ToArray());
            return (long)(await query.ExecuteScalarAsync(ct))!;
        }
        try
        {
            var refused = await Source(tenant, card, actor); eventIds.Add(refused.EventId);
            try { await quota.TryReserveAsync(refused, ct); throw new InvalidOperationException("Unowned quota accepted."); }
            catch (InvalidOperationException e) when (e.Message == "Mass mention quota requires the owning Work transaction.") { }
            Require((await Reserve(refused, true)).ErrorCode == "fixture_refused" && await Count(tenant) == 0,
                "Refused owning command consumed durable group quota.");
            var committed = new List<WorkEvent>();
            for (var n = 0; n < ICardMassMentionQuota.MaximumReservations; n++)
            {
                var source = await Source(tenant, card, actor); eventIds.Add(source.EventId); committed.Add(source);
                Require((await Reserve(source)).Succeeded && (await Reserve(source)).Succeeded, "Quota reservation or exact retry failed.");
            }
            Require(await Count(tenant) == 3, "Exact retries duplicated quota or a committed reservation was lost.");
            var fourth = await Source(tenant, card, actor); eventIds.Add(fourth.EventId);
            Require((await Reserve(fourth)).ErrorCode == "mass_mention_rate_limited" && await Count(tenant) == 3,
                "Fourth mass delivery bypassed the rolling quota.");
            await using (var missingEvent = new NpgsqlCommand("SELECT count(*) FROM work_events WHERE tenant_id=@tenant AND event_id=@event;", admin))
            {
                missingEvent.Parameters.AddWithValue("tenant", tenant); missingEvent.Parameters.AddWithValue("event", fourth.EventId);
                Require((long)(await missingEvent.ExecuteScalarAsync(ct))! == 0, "Rate refusal retained the source event.");
            }
            try
            {
                await Reserve(committed[0] with { CreatedAt = committed[0].CreatedAt.AddSeconds(1) }, append: false);
                throw new InvalidOperationException("Quota source identity mutation was accepted.");
            }
            catch (InvalidOperationException e) when (e.Message == "Mass mention reservation identity was reused.") { }
            var foreign = await Source(foreignTenant, foreignCard, foreignActor); eventIds.Add(foreign.EventId);
            Require((await Reserve(foreign)).Succeeded && await Count(foreignTenant) == 1, "Another tenant shared the original quota.");
            await using (var foreignRead = await provider.GetRequiredService<PostgresConnectionFactory>().OpenTenantSessionAsync(foreignTenant, ct))
            await using (var query = new NpgsqlCommand("SELECT count(*) FROM mass_mention_reservations WHERE tenant_id=@original;", foreignRead.Connection, foreignRead.Transaction))
            {
                query.Parameters.AddWithValue("original", tenant);
                Require((long)(await query.ExecuteScalarAsync(ct))! == 0, "Restricted quota read disclosed a foreign tenant's reservations.");
            }
            var noSource = foreign with { EventId = Guid.NewGuid() }; eventIds.Add(noSource.EventId);
            Require((await Reserve(noSource, append: false)).ErrorCode == "work_storage_unavailable", "Quota admitted an absent source event.");
            // Trusted fixture ages reservations; runtime has neither UPDATE nor
            // trigger-control authority. Verify the actual rolling window query.
            await using (var age = new NpgsqlCommand("""
                ALTER TABLE mass_mention_reservations DISABLE TRIGGER mass_mention_reservation_guard;
                UPDATE mass_mention_reservations SET reserved_at=clock_timestamp()-interval '11 minutes'
                 WHERE tenant_id=@tenant AND event_id=ANY(@events);
                ALTER TABLE mass_mention_reservations ENABLE TRIGGER mass_mention_reservation_guard;
                """, admin))
            {
                age.Parameters.AddWithValue("tenant", tenant); age.Parameters.AddWithValue("events", committed.Select(row => row.EventId).ToArray());
                await age.ExecuteNonQueryAsync(ct);
            }
            Require((await Reserve(fourth)).Succeeded && await Count(tenant) == 4, "Expired rolling reservations did not admit new delivery.");
            Require((await Reserve(committed[0])).Succeeded && await Count(tenant) == 4, "Historical exact reservation retry consumed fresh quota.");
            var competing = new List<WorkEvent>();
            for (var n = 0; n < 4; n++)
            {
                var source = await Source(tenant, card, actor); eventIds.Add(source.EventId); competing.Add(source);
                var prepared = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true), async () =>
                { await events.AppendAsync(source, ct); return WorkOperation<bool>.Success(true); }, ct);
                Require(prepared.Succeeded, "Competing quota source preparation failed.");
            }
            var competed = await Task.WhenAll(competing.Select(source => Reserve(source, append: false)));
            Require(competed.Count(result => result.Succeeded) == 2 && competed.Count(result => result.ErrorCode == "mass_mention_rate_limited") == 2,
                "Concurrent quota writers admitted more than the two remaining reservations.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("ALTER TABLE mass_mention_reservations ENABLE TRIGGER mass_mention_reservation_guard; DELETE FROM mass_mention_reservations WHERE event_id=ANY(@events);", admin);
            cleanup.Parameters.AddWithValue("events", eventIds.ToArray()); await cleanup.ExecuteNonQueryAsync(ct);
        }
        Console.WriteLine("Restricted mass mention quota: owning scope, exact source/retry, three per actor/Board rolling window, rate-refusal event rollback, tenant independence, expiry and original retry preservation passed.");
    }
}
