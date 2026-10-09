using Npgsql;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Persistence;

// ARCH-04/11: prove readiness against the real migration ledger using restricted API and Worker logins.
internal static class RuntimeSchemaReadinessContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        await using var api = new PostgresConnectionFactory(apiConnection);
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var factories = new[] { api, worker };
        foreach (var factory in factories)
        { await using var complete = await factory.OpenConnectionAsync(ct); }
        foreach (var version in new[] { "001_foundation", "078_navigation_interaction_sources", "081_navigation_original_recovery", "082_organization_metadata_replays", "083_organization_departure_replays", "084_organization_removal_replays", "085_interaction_actor_lock_order", "086_organization_creation_replays", "087_organization_deletion_replays", "088_organization_deletion_progress","089_organization_deletion_terminal","090_organization_lifecycle_delivery","091_organization_deletion_candidates","092_organization_deletion_pages","093_organization_deletion_discovery","094_organization_metadata_events","095_organization_metadata_delivery","096_organization_metadata_discovery","097_organization_member_addition_events","098_organization_member_removal_events","099_organization_member_invitation_events","100_organization_invitation_revocation_events","101_organization_invitation_acceptance_events","102_invitation_recipient_events","103_invitation_recipient_unpublished_cleanup","104_invitation_recipient_retained_board_admin","105_invitation_recipient_authority","106_invitation_recipient_authority_discovery","107_invitation_recipient_board_authority","108_invitation_recipient_organization_lifecycle","109_invitation_issuer_account_authority","110_invitation_issuer_authority_exhaustion","111_notification_batch_source_guard","112_work_archive_history", "113_invitation_recipient_membership_authority","114_identity_lifecycle_clocks","115_invitation_issuer_job_clocks","116_invitation_authority_page_clocks","117_entity_route_clocks","118_entity_route_clock_admission","119_invitation_route_clocks","120_organization_access_route_clocks","121_organization_event_delivery_clocks","122_organization_metadata_stream_clocks" })
        {
            var hidden = $"contract_missing_{Guid.NewGuid():N}";
            await using var hide = new NpgsqlCommand("UPDATE public.schema_migrations SET version=@hidden WHERE version=@version", admin);
            hide.Parameters.AddWithValue("hidden", hidden); hide.Parameters.AddWithValue("version", version);
            if (await hide.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Required migration fixture was absent.");
            try
            {
                foreach (var factory in factories)
                {
                    var rejected = false;
                    try { await using var connection = await factory.OpenConnectionAsync(ct); }
                    catch (RuntimeDatabaseSchemaException) { rejected = true; }
                    if (!rejected) throw new InvalidOperationException("Restricted runtime admitted an incomplete migration ledger.");
                }
            }
            finally
            {
                await using var restore = new NpgsqlCommand("UPDATE public.schema_migrations SET version=@version WHERE version=@hidden", admin);
                restore.Parameters.AddWithValue("hidden", hidden); restore.Parameters.AddWithValue("version", version);
                if (await restore.ExecuteNonQueryAsync(CancellationToken.None) != 1)
                    throw new InvalidOperationException("Migration readiness fixture could not restore the ledger.");
            }
            foreach (var factory in factories)
            { await using var recovered = await factory.OpenConnectionAsync(ct); }
        }
        Console.WriteLine("Restricted API/Worker schema readiness: complete ledger accepted, missing required migrations refused, restored ledger recovered.");
    }
}
