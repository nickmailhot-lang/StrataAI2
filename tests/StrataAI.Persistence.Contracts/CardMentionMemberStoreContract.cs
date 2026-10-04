using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;

// Real restricted tenant storage; null actor/fixture admission is deliberately
// synthetic. This does not prove HTTP cookie or recipient publication policy.
internal static class CardMentionMemberStoreContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid foreignTenant,
        Guid board, Guid foreignBoard, Guid foreignUser, Guid card, CancellationToken ct)
    {
        // Earlier Worker contracts deliberately move this Card. Seed membership
        // against its current canonical parent rather than the original Board.
        await using (var parent = new NpgsqlCommand("SELECT board_id FROM cards WHERE tenant_id=@tenant AND id=@card;", admin))
        {
            parent.Parameters.AddWithValue("tenant", tenant); parent.Parameters.AddWithValue("card", card);
            board = (Guid)(await parent.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Mention fixture Card is unavailable."));
        }
        var users = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
        var prefix = "mention_" + Guid.NewGuid().ToString("N")[..8] + "_";
        var names = Enumerable.Range(1, 30).Select(n => prefix + n.ToString("D2")).ToArray();
        var groupComments = new List<Guid>();
        var store = provider.GetRequiredService<ICardMentionMemberStore>(); var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        async Task<T> Scope<T>(Guid scope, Func<Task<T>> operation)
        {
            var result = await unit.ExecuteReadAsync(scope, null, "fixture_denied", () => Task.FromResult(true),
                async () => WorkOperation<T>.Success(await operation()), ct);
            Require(result.Succeeded, "Mention metadata owning read failed."); return result.Value!;
        }
        try
        {
            await using (var transaction = await admin.BeginTransactionAsync(ct))
            {
                await using var seed = new NpgsqlCommand("""
                    INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
                    SELECT id,'mention-'||id::text||'@example.test',upper('mention-'||id::text||'@example.test'),
                      'Same display name',CASE WHEN n=30 THEN 'SUSPENDED' ELSE 'ACTIVE' END,n<>29,'fixture',statement_timestamp(),statement_timestamp()
                    FROM unnest(@users) WITH ORDINALITY t(id,n);
                    INSERT INTO organization_members(id,tenant_id,user_id,role,status)
                    SELECT gen_random_uuid(),@tenant,id,'MEMBER',CASE WHEN n=28 THEN 'REMOVED' ELSE 'ACTIVE' END
                    FROM unnest(@users) WITH ORDINALITY t(id,n);
                    INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
                    SELECT gen_random_uuid(),@tenant,@board,id,'MEMBER',CASE WHEN n=27 THEN 'REMOVED' ELSE 'ACTIVE' END,statement_timestamp(),statement_timestamp()
                    FROM unnest(@users) WITH ORDINALITY t(id,n) WHERE n<>26;
                    INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),@foreign,@shared,'MEMBER','ACTIVE');
                    INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
                    VALUES(gen_random_uuid(),@foreign,@foreign_board,@shared,'MEMBER','ACTIVE',statement_timestamp(),statement_timestamp());
                    """, admin, transaction);
                seed.Parameters.AddWithValue("users", users); seed.Parameters.AddWithValue("tenant", tenant);
                seed.Parameters.AddWithValue("board", board); seed.Parameters.AddWithValue("foreign", foreignTenant);
                seed.Parameters.AddWithValue("foreign_board", foreignBoard); seed.Parameters.AddWithValue("shared", users[0]);
                await seed.ExecuteNonQueryAsync(ct);
                for (var index = 0; index < users.Length; index++)
                {
                    await using var claim = new NpgsqlCommand("""
                        SELECT set_config('app.identity_subject',@subject,true);
                        UPDATE user_mention_handles SET handle=@handle,version=2,updated_at=clock_timestamp() WHERE user_id=@user;
                        """, admin, transaction);
                    claim.Parameters.AddWithValue("subject", users[index].ToString()); claim.Parameters.AddWithValue("user", users[index]);
                    claim.Parameters.AddWithValue("handle", names[index]); await claim.ExecuteNonQueryAsync(ct);
                }
                await transaction.CommitAsync(ct);
            }
            try { await store.SearchAsync(tenant, board, prefix, null, true, ct); throw new InvalidOperationException("Unowned mention search accepted."); }
            catch (InvalidOperationException error) when (error.Message == "Mention members require an owning authorized Work transaction.") { }
            try { await store.ResolveAsync(tenant, board, names, true, ct); throw new InvalidOperationException("Unowned mention resolution accepted."); }
            catch (InvalidOperationException error) when (error.Message == "Mention members require an owning authorized Work transaction.") { }
            var first = await Scope(tenant, () => store.SearchAsync(tenant, board, prefix, null, true, ct));
            Require(first.Count == 21 && first.Select(x => x.Handle).SequenceEqual(names.Take(21))
                && first.All(x => x.HandleVersion == 2 && x.DisplayName == "Same display name"),
                "Literal prefix/current handle/normalized ordinal lookahead metadata failed.");
            var tail = await Scope(tenant, () => store.SearchAsync(tenant, board, prefix, first[19].Handle, true, ct));
            Require(tail.Select(x => x.Handle).SequenceEqual(names.Skip(20).Take(5))
                && first.Take(20).Concat(tail).Select(x => x.UserId).Distinct().Count() == 25,
                "Mention seek duplicated or lost active participants.");
            var resolved = await Scope(tenant, () => store.ResolveAsync(tenant, board, names, true, ct));
            var mass = provider.GetRequiredService<ICardMassMentionMemberStore>();
            var group = await Scope(tenant, () => mass.LockRecipientsAsync(tenant, board, card, false, true, true, ct));
            Require(group.Intersect(users).Count() == 25 && group.Count > 20, "Full Board group roster was truncated or widened to ineligible users.");
            Require((await Scope(tenant, () => mass.LockRecipientsAsync(tenant, foreignBoard, card, false, true, true, ct))).Count == 0,
                "Mass roster crossed the canonical Card/Board parent.");
            await using (var assign = new NpgsqlCommand("""
                INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by)
                SELECT @tenant,@board,@card,id,@assigner FROM unnest(@assigned) AS targets(id);
                """, admin))
            {
                assign.Parameters.AddWithValue("tenant", tenant); assign.Parameters.AddWithValue("board", board);
                assign.Parameters.AddWithValue("card", card); assign.Parameters.AddWithValue("assigner", users[0]);
                // Retained assignments include departed/suspended/unverified
                // participants: current roster admission must still filter them.
                assign.Parameters.AddWithValue("assigned", new[] { users[0], users[1], users[2], users[26], users[27], users[28], users[29] });
                await assign.ExecuteNonQueryAsync(ct);
            }
            var assignedGroup = await Scope(tenant, () => mass.LockRecipientsAsync(tenant, board, card, true, false, true, ct));
            Require(assignedGroup.Intersect(users).Order().SequenceEqual(users.Take(3).Order()) && assignedGroup.All(group.Contains),
                "Card group omitted a current assignee or widened to stale/unassigned Board participants.");
            var unverifiedGroup = await Scope(tenant, () => mass.LockRecipientsAsync(tenant, board, card, true, false, false, ct));
            Require(unverifiedGroup.Intersect(users).Order().SequenceEqual(users.Take(3).Append(users[28]).Order()),
                "Card group ignored its explicit verified-email policy.");
            Require((await Scope(tenant, () => mass.LockRecipientsAsync(tenant, board, card, true, true, true, ct))).SequenceEqual(group),
                "Overlapping Card/Board group scopes duplicated or omitted recipients.");
            Require((await Scope(tenant, () => mass.LockRecipientsAsync(tenant, board, Guid.NewGuid(), true, true, true, ct))).Count == 0
                && (await Scope(tenant, () => mass.LockRecipientsAsync(tenant, board, card, false, false, true, ct))).Count == 0
                && (await Scope(foreignTenant, () => mass.LockRecipientsAsync(foreignTenant, board, card, true, true, true, ct))).Count == 0,
                "Missing Card, absent scopes or foreign tenant disclosed a group roster.");
            var groupComment = Guid.NewGuid();
            var snapshotStore = provider.GetRequiredService<ICommentMentionSnapshotStore>(); var commentStore = provider.GetRequiredService<ICardCommentStore>();
            var refusedGroup = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true), async () =>
            {
                var row = await commentStore.CreateAsync(groupComment, tenant, card, group[0], "Internal group history fixture", DateTimeOffset.UtcNow, ct);
                var snapshot = new CommentMentionSnapshot(tenant, card, row.Id, row.Version, row.UpdatedAt, group);
                await snapshotStore.AppendSnapshotAsync(snapshot, ct); await snapshotStore.AppendSnapshotAsync(snapshot, ct);
                Require((await snapshotStore.FindSnapshotAsync(tenant, card, row.Id, row.Version, ct))!.SameAs(snapshot),
                    "Complete group snapshot was truncated, duplicated or changed on read.");
                var parent = await provider.GetRequiredService<IWorkManagementStore>().FindCardAsync(card, ct);
                var change = new WorkEvent(Guid.NewGuid(), tenant, board, group[0], "MENTION_CREATED", "Card", card,
                    parent!.Version, "mass-storage-fixture", row.UpdatedAt);
                await provider.GetRequiredService<IWorkEventStore>().AppendAsync(change, ct);
                var notifications = provider.GetRequiredService<IWorkNotificationStore>();
                await notifications.AppendCardMentionsAsync(change, group, ct);
                await notifications.AppendCardMentionsAsync(change, group, ct);
                foreach (var recipient in group.Skip(1))
                {
                    var items = await notifications.ListCardNotificationsAsync(tenant, recipient, cancellationToken: ct);
                    Require(items.Count(item => item.EventId == change.EventId) == 1, "Batch group notification recipient was omitted or duplicated.");
                }
                Require(!(await notifications.ListCardNotificationsAsync(tenant, group[0], cancellationToken: ct)).Any(item => item.EventId == change.EventId),
                    "Batch group actor received a self notification.");
                return WorkOperation<bool>.Failure("fixture_refused");
            }, ct);
            Require(refusedGroup.ErrorCode == "fixture_refused"
                && await Scope(tenant, () => commentStore.FindAsync(tenant, card, groupComment, ct)) is null
                && await Scope(tenant, () => snapshotStore.FindSnapshotAsync(tenant, card, groupComment, 1, ct)) is null,
                "Refused full group history retained comment/snapshot effects.");
            var retainedGroup = await Scope(tenant, async () =>
            {
                var id = Guid.NewGuid(); groupComments.Add(id);
                var row = await commentStore.CreateAsync(id, tenant, card, group[0], "Committed internal group history", DateTimeOffset.UtcNow, ct);
                var snapshot = new CommentMentionSnapshot(tenant, card, id, row.Version, row.UpdatedAt, group);
                await snapshotStore.AppendSnapshotAsync(snapshot, ct); return snapshot;
            });
            Require((await Scope(tenant, () => snapshotStore.FindSnapshotAsync(tenant, card, retainedGroup.CommentId, 1, ct)))!.SameAs(retainedGroup),
                "Complete group recipient history did not survive its actual database commit.");
            Require(resolved.Select(x => x.Handle).SequenceEqual(names.Take(25)),
                "Removed Board/Organization, Organization-only, suspended or unverified account reached resolution.");
            var unverified = await Scope(tenant, () => store.ResolveAsync(tenant, board, names, false, ct));
            Require(unverified.Count == 26 && unverified.Any(x => x.UserId == users[28]), "Verified-email policy was not explicit.");
            Require((await Scope(tenant, () => store.ResolveAsync(tenant, board, new[] { $"u_{users[0]:N}", $"u_{foreignUser:N}", "unknown_handle" }, true, ct))).Count == 0,
                "Former alias, unknown or foreign account became an eligible current handle.");
            Require((await Scope(foreignTenant, () => store.SearchAsync(foreignTenant, board, prefix, null, true, ct))).Count == 0
                && (await Scope(tenant, () => store.ResolveAsync(tenant, foreignBoard, names, true, ct))).Count == 0,
                "Shared user membership widened another tenant/Board scope.");
            var shared = await Scope(foreignTenant, () => store.ResolveAsync(foreignTenant, foreignBoard, new[] { names[0] }, true, ct));
            Require(shared.Count == 1 && shared[0].UserId == users[0], "Explicit shared Board participant did not resolve in its own tenant.");
            var exact = await Scope(tenant, () => store.ResolveAsync(tenant, board, new[] { names[0], names[0] }, true, ct));
            Require(exact.Count == 1, "Repeated targets duplicated an account.");
            var planning = new CardCommentMentionPlanning(store, new(false, true, 12, TimeSpan.FromDays(1), TimeSpan.FromHours(1)));
            var planned = await Scope(tenant, () => planning.ResolveAsync(tenant, board, users[0],
                $"  @{names[0]} @{names[1]} @{names[2]} @{names[2]} @{names[25]} @{names[26]} @{names[27]} @{names[28]} @{names[29]} @u_{users[0]:N} @card @board  ", [users[1]], ct));
            Require(planned.Succeeded && planned.Value!.Recipients.References.Count == 4
                && planned.Value.Recipients.Current.SequenceEqual(new[] { users[0], users[1], users[2] }.Order())
                && planned.Value.Recipients.Added.SequenceEqual(new[] { users[2] })
                && planned.Value.HasCardMention && planned.Value.HasBoardMention,
                "Current-scoped comment plan disclosed an ineligible/former recipient, lost reference identity or repeated/self notification delta.");
            var excess = await Scope(tenant, () => planning.ResolveAsync(tenant, board, users[0], string.Join(' ', names.Take(21).Select(name => "@" + name)), [], ct));
            Require(excess.ErrorCode == "invalid_comment_mentions", "Unbounded resolved comment recipients were admitted.");
            await Scope(tenant, async () =>
            {
                Require((await planning.RevalidateAsync(tenant, board, planned.Value!, ct)).Succeeded, "Current mention target revalidation failed.");
                foreach (var statement in new[] {
                    "UPDATE users SET display_name=display_name WHERE id=@user",
                    "UPDATE user_mention_handles SET version=version WHERE user_id=@user",
                    "UPDATE organization_members SET status=status WHERE tenant_id=@tenant AND user_id=@user",
                    "UPDATE board_members SET status=status WHERE tenant_id=@tenant AND board_id=@board AND user_id=@user" })
                {
                    // An opened connection may redact its password property.
                    // Keep using the original CI-only administrative setting.
                    var independentAdmin = Environment.GetEnvironmentVariable("STRATAAI_CONTRACT_ADMIN_CONNECTION")
                        ?? throw new InvalidOperationException("Contract admin connection is required.");
                    await using var competitor = new NpgsqlConnection(independentAdmin); await competitor.OpenAsync(ct);
                    await using var competingTransaction = await competitor.BeginTransactionAsync(ct);
                    await using var configure = new NpgsqlCommand("SET LOCAL lock_timeout='250ms'; SELECT set_config('app.identity_subject',@subject,true);", competitor, competingTransaction);
                    configure.Parameters.AddWithValue("subject", users[0].ToString()); await configure.ExecuteNonQueryAsync(ct);
                    await using var mutation = new NpgsqlCommand(statement, competitor, competingTransaction) { CommandTimeout = 5 };
                    mutation.Parameters.AddWithValue("user", users[0]); mutation.Parameters.AddWithValue("tenant", tenant); mutation.Parameters.AddWithValue("board", board);
                    try { await mutation.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Current recipient lock did not retain account/handle/membership eligibility."); }
                    catch (PostgresException e) when (e.SqlState == "55P03") { }
                    await competingTransaction.RollbackAsync(ct);
                }
                return true;
            });
            await using (var rename = new NpgsqlCommand("""
                SELECT set_config('app.identity_subject',@subject,false);
                UPDATE user_mention_handles SET handle=@handle,version=version+1,updated_at=clock_timestamp() WHERE user_id=@user;
                """, admin))
            {
                rename.Parameters.AddWithValue("subject", users[2].ToString()); rename.Parameters.AddWithValue("user", users[2]);
                rename.Parameters.AddWithValue("handle", prefix + "renamed"); await rename.ExecuteNonQueryAsync(ct);
            }
            Require((await Scope(tenant, () => planning.RevalidateAsync(tenant, board, planned.Value!, ct))).ErrorCode == "mention_targets_changed",
                "Retired recipient handle still authorized original plan.");
            await Scope(tenant, async () =>
            {
                foreach (var invalid in new[] { "@member", "nïck", "1name", new string('x', 41) })
                {
                    try { await store.SearchAsync(tenant, board, invalid, null, true, ct); throw new InvalidOperationException("Invalid prefix accepted."); }
                    catch (ArgumentException) { }
                }
                try { await store.ResolveAsync(tenant, board, Enumerable.Repeat(names[0], 65).ToArray(), true, ct); throw new InvalidOperationException("Unbounded targets accepted."); }
                catch (ArgumentException) { }
                return true;
            });
        }
        finally
        {
            await using var cleanupTransaction = await admin.BeginTransactionAsync(ct);
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM comment_mention_recipients WHERE tenant_id=@tenant AND comment_id=ANY(@comments);
                DELETE FROM comment_mention_snapshots WHERE tenant_id=@tenant AND comment_id=ANY(@comments);
                DELETE FROM card_comments WHERE tenant_id=@tenant AND id=ANY(@comments);
                DELETE FROM card_members WHERE tenant_id=@tenant AND (user_id=ANY(@users) OR assigned_by=ANY(@users));
                DELETE FROM board_members WHERE user_id=ANY(@users) AND tenant_id IN (@tenant,@foreign);
                DELETE FROM organization_members WHERE user_id=ANY(@users) AND tenant_id IN (@tenant,@foreign);
                DELETE FROM users WHERE id=ANY(@users);
                """, admin, cleanupTransaction);
            cleanup.Parameters.AddWithValue("comments", groupComments.ToArray());
            cleanup.Parameters.AddWithValue("users", users); cleanup.Parameters.AddWithValue("tenant", tenant); cleanup.Parameters.AddWithValue("foreign", foreignTenant);
            await cleanup.ExecuteNonQueryAsync(ct);
            await cleanupTransaction.CommitAsync(ct);
        }
        Console.WriteLine("Restricted mention member metadata: owning tenant, literal prefix/seek bounds, exact current handles, active Board/Organization/account/email policy, former alias exclusion and shared-user isolation passed.");
        Console.WriteLine("Restricted mass recipient history: complete Board roster beyond username window, current Card assignments with explicit email policy, union without duplicates, Card/Board/tenant affinity, durable full recipient snapshot and refused comment/history rollback passed.");
    }
}
