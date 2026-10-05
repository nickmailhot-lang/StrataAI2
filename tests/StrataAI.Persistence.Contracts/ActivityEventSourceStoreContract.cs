using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;

internal static class ActivityEventSourceStoreContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid actor, CancellationToken ct)
    {
        var board = Guid.NewGuid(); var otherBoard = Guid.NewGuid(); var personalOwner = Guid.NewGuid();
        var at = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
        var sources = provider.GetRequiredService<IActivityEventSourceStore>(); var events = provider.GetRequiredService<IWorkEventStore>();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>(); string originalCaption; string originalStatus;
        var feed = provider.GetRequiredService<IActivityFeedStore>();
        await using (var profile = new NpgsqlCommand("SELECT display_name,status FROM users WHERE id=@actor;", admin))
        {
            profile.Parameters.AddWithValue("actor", actor); await using var row = await profile.ExecuteReaderAsync(ct);
            Require(await row.ReadAsync(ct), "Activity actor fixture is unavailable."); originalCaption = row.GetString(0); originalStatus = row.GetString(1);
        }
        async Task<T> Scope<T>(Func<Task<T>> action)
        {
            var result = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true),
                async () => WorkOperation<T>.Success(await action()), ct);
            Require(result.Succeeded, "Activity owning operation failed."); return result.Value!;
        }
        await using (var seed = new NpgsqlCommand("INSERT INTO boards(id,tenant_id,name,visibility,created_at,updated_at) VALUES(@board,@tenant,'Activity source contract','ORGANIZATION',@at,@at),(@other_board,@tenant,'Historical activity source contract','ORGANIZATION',@at,@at);", admin))
        {
            seed.Parameters.AddWithValue("board", board); seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("at", at);
            seed.Parameters.AddWithValue("other_board", otherBoard);
            await seed.ExecuteNonQueryAsync(ct);
        }
        try
        {
            try { await sources.ReadBoardWindowAsync(tenant, board, null, null, ct); throw new InvalidOperationException("Unowned activity source read accepted."); }
            catch (InvalidOperationException exception) when (exception.Message == "Activity sources require the owning Work transaction.") { }
            var recorded = new List<WorkEvent>();
            await Scope(async () =>
            {
                for (var index = 1; index <= 65; index++)
                {
                    var change = new WorkEvent(Guid.NewGuid(), tenant, board, actor, "BOARD_UPDATED", "Board", board, index, "activity-source-contract", at);
                    recorded.Add(change); await events.AppendAsync(change, ct);
                }
                return true;
            });
            var first = await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct));
            Require(first.Count == 51 && first.All(row => row.ActorLabel == originalCaption && row.Metadata.Count == 0), "Activity bounded window or captured attribution failed.");
            Require(first.Select(row => row.EventId).SequenceEqual(recorded.Select(row => row.EventId).OrderByDescending(id => id.ToString("N"), StringComparer.Ordinal).Take(51)),
                "Activity same-time source identities use inconsistent ordering.");
            var anchor = first[49]; var tail = await Scope(() => sources.ReadBoardWindowAsync(tenant, board, anchor.CreatedAt, anchor.EventId, ct));
            Require(tail.Count == 15 && first.Take(50).Concat(tail).Select(row => row.EventId).Distinct().Count() == 65, "Activity timestamp/ID seek lost or duplicated a tie.");
            var binding = new ActivityCursorBinding(tenant, actor, ActivityTargetKind.Board, board);
            var visible = await Scope(() => feed.ReadAsync(binding, null, ct));
            Require(visible.SequenceEqual(first), "Restricted SQL activity visibility changed the admitted Board window.");
            Require((await Scope(() => feed.ReadAsync(binding, new(anchor.CreatedAt, anchor.EventId), ct))).SequenceEqual(tail),
                "Restricted SQL activity visibility changed the seek tail.");
            Require((await Scope(() => feed.ReadAsync(binding with { ViewerId = Guid.NewGuid() }, null, ct))).Count == 0,
                "Restricted SQL activity admitted a nonmember.");
            Require(first.SequenceEqual(await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct))), "Activity repeated read changed historical fields.");
            await using (var rename = new NpgsqlCommand("UPDATE users SET display_name='Later activity actor',status='DEACTIVATED' WHERE id=@actor;", admin))
            { rename.Parameters.AddWithValue("actor", actor); await rename.ExecuteNonQueryAsync(ct); }
            await Scope(async () => { await events.AppendAsync(recorded[0], ct); return true; });
            Require((await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct))).All(row => row.ActorLabel == originalCaption),
                "Activity read/retry rebound history to a renamed/deactivated account.");
            var failed = new WorkEvent(Guid.NewGuid(), tenant, board, actor, "BOARD_UPDATED", "Board", board, 66, "activity-source-rollback", at.AddSeconds(1));
            var refused = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true), async () =>
            { await events.AppendAsync(failed, ct); return WorkOperation<bool>.Failure("fixture_refused"); }, ct);
            Require(refused.ErrorCode == "fixture_refused" && !(await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct))).Any(row => row.EventId == failed.EventId),
                "Activity source survived owning rollback.");
            await Scope(async () => { await events.AppendAsync(failed, ct); return true; });
            Require((await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct)))[0].ActorLabel == "Later activity actor", "New activity did not capture its own event-time caption.");
            Require((await Scope(() => sources.ReadBoardWindowAsync(tenant, Guid.NewGuid(), null, null, ct))).Count == 0, "Activity Board affinity widened.");
            try { await sources.ReadCardWindowAsync(tenant, board, board, null, null, ct); throw new InvalidOperationException("Unowned Card activity source read accepted."); }
            catch (InvalidOperationException exception) when (exception.Message == "Activity sources require the owning Work transaction.") { }
            // Raw Card identities intentionally overlap Board identities: entity
            // type and source-Board boundaries must filter before the row limit.
            var cardRecorded = new List<WorkEvent>();
            await Scope(async () =>
            {
                for (var index = 1; index <= 65; index++)
                {
                    var change = new WorkEvent(Guid.NewGuid(), tenant, board, actor, "CARD_UPDATED", "Card", board, index, "card-activity-source-contract", at.AddSeconds(2));
                    cardRecorded.Add(change); await events.AppendAsync(change, ct);
                }
                await events.AppendAsync(new(Guid.NewGuid(), tenant, board, actor, "BOARD_UPDATED", "Board", board, 67, "card-activity-type-boundary", at.AddSeconds(3)), ct);
                await events.AppendAsync(new(Guid.NewGuid(), tenant, board, actor, "CARD_UPDATED", "Card", Guid.NewGuid(), 1, "card-activity-identity-boundary", at.AddSeconds(3)), ct);
                await events.AppendAsync(new(Guid.NewGuid(), tenant, otherBoard, actor, "CARD_UPDATED", "Card", board, 66, "card-activity-historical-board", at.AddSeconds(3)), ct);
                return true;
            });
            var cardFirst = await Scope(() => sources.ReadCardWindowAsync(tenant, board, board, null, null, ct));
            Require(cardFirst.Count == 51 && cardFirst.All(row => row.EntityType == "Card" && row.EntityId == board && row.BoardId == board), "Card activity source type/identity/Board boundary or bound failed.");
            Require(cardFirst.Select(row => row.EventId).SequenceEqual(cardRecorded.Select(row => row.EventId).OrderByDescending(id => id.ToString("N"), StringComparer.Ordinal).Take(51)), "Card activity tied order failed.");
            var cardAnchor = cardFirst[49];
            var cardTail = await Scope(() => sources.ReadCardWindowAsync(tenant, board, board, cardAnchor.CreatedAt, cardAnchor.EventId, ct));
            Require(cardTail.Count == 15 && cardFirst.Take(50).Concat(cardTail).Select(row => row.EventId).Distinct().Count() == 65, "Card activity tied seek lost or duplicated a source.");
            Require((await Scope(() => sources.ReadCardWindowAsync(tenant, otherBoard, board, null, null, ct))).Count == 1, "Explicit historical Card Board slice was unavailable.");
            Require((await Scope(() => sources.ReadCardWindowAsync(tenant, board, Guid.NewGuid(), null, null, ct))).Count == 0, "Card activity identity widened.");
            // Materialize the previously synthetic identity in the other Board:
            // these historical sources are deliberate fixtures, not a move command.
            await using (var current = new NpgsqlCommand("""
                UPDATE users SET status=@status WHERE id=@actor;
                INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
                  VALUES(@list,@tenant,@other,'Activity current List','500000000000000000000000000000',@at,@at);
                INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
                  VALUES(@card,@tenant,@other,@list,'Activity current Card','500000000000000000000000000000',@at,@at);
                """, admin))
            {
                current.Parameters.AddWithValue("status", originalStatus); current.Parameters.AddWithValue("actor", actor);
                current.Parameters.AddWithValue("list", Guid.NewGuid()); current.Parameters.AddWithValue("tenant", tenant);
                current.Parameters.AddWithValue("other", otherBoard); current.Parameters.AddWithValue("card", board);
                current.Parameters.AddWithValue("at", at); await current.ExecuteNonQueryAsync(ct);
            }
            var cardBinding = new ActivityCursorBinding(tenant, actor, ActivityTargetKind.Card, board);
            var history = await Scope(() => feed.ReadAsync(cardBinding, null, ct));
            Require(history.Count == 51 && history[0].BoardId == otherBoard && history.Skip(1).All(row => row.BoardId == board),
                "Authorized Card candidate query narrowed history to its current Board.");
            var historyAnchor = history[49];
            var historyTail = await Scope(() => feed.ReadAsync(cardBinding, new(historyAnchor.CreatedAt, historyAnchor.EventId), ct));
            Require(historyTail.Count == 16 && history.Take(50).Concat(historyTail).Select(row => row.EventId).Distinct().Count() == 66,
                "Authorized cross-Board Card history seek lost or duplicated an event.");
            await using (var hideSource = new NpgsqlCommand("UPDATE boards SET visibility='PRIVATE' WHERE tenant_id=@tenant AND id=@board;", admin))
            {
                hideSource.Parameters.AddWithValue("tenant", tenant); hideSource.Parameters.AddWithValue("board", board);
                await hideSource.ExecuteNonQueryAsync(ct);
            }
            var admittedCurrent = await Scope(() => feed.ReadAsync(cardBinding, null, ct));
            Require(admittedCurrent.Count == 1 && admittedCurrent[0].BoardId == otherBoard,
                "Card history admitted an inaccessible historical source Board.");
            await using (var hideCurrent = new NpgsqlCommand("UPDATE boards SET visibility='PRIVATE' WHERE tenant_id=@tenant AND id=@board;", admin))
            {
                hideCurrent.Parameters.AddWithValue("tenant", tenant); hideCurrent.Parameters.AddWithValue("board", otherBoard);
                await hideCurrent.ExecuteNonQueryAsync(ct);
            }
            Require((await Scope(() => feed.ReadAsync(cardBinding, null, ct))).Count == 0,
                "Card history admitted an inaccessible current Board.");
            await using (var personalFixture = new NpgsqlCommand("""
                UPDATE boards SET visibility='ORGANIZATION' WHERE tenant_id=@tenant AND id=ANY(@boards);
                INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
                  VALUES(@owner,@email,upper(@email),'Private activity owner','ACTIVE','unused-contract-hash',@at,@at);
                INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(@owner,@tenant,@owner,'MEMBER','ACTIVE');
                """, admin))
            {
                personalFixture.Parameters.AddWithValue("tenant", tenant); personalFixture.Parameters.AddWithValue("boards", new[] { board, otherBoard });
                personalFixture.Parameters.AddWithValue("owner", personalOwner); personalFixture.Parameters.AddWithValue("email", $"activity-{personalOwner:N}@example.test");
                personalFixture.Parameters.AddWithValue("at", at); await personalFixture.ExecuteNonQueryAsync(ct);
            }
            var ownPrivate = new HashSet<Guid>(); var hiddenPrivate = new HashSet<Guid>();
            await Scope(async () =>
            {
                var work = provider.GetRequiredService<IWorkManagementStore>();
                var watches = provider.GetRequiredService<IWatchSubscriptionStore>();
                var reminders = provider.GetRequiredService<ICardReminderStore>();
                var currentCard = await work.FindCardAsync(board, ct) ?? throw new InvalidOperationException("Activity private Card fixture missing.");
                var ownWatch = await watches.SetAsync(tenant, actor, "CARD", board, true, 0, at, ct)
                    ?? throw new InvalidOperationException("Own activity Watch fixture failed.");
                var hiddenWatch = await watches.SetAsync(tenant, personalOwner, "CARD", board, true, 0, at, ct)
                    ?? throw new InvalidOperationException("Other activity Watch fixture failed.");
                var ownReminder = await reminders.SetAsync(currentCard, actor, "AT_DUE", true, 0, at, ct)
                    ?? throw new InvalidOperationException("Own activity Reminder fixture failed.");
                var watchSource = new WorkEvent(Guid.NewGuid(), tenant, board, personalOwner, "WATCH_CREATED", "WatchSubscription", ownWatch.Id, ownWatch.Version, "private-feed-owner", at.AddSeconds(4));
                var reminderSource = new WorkEvent(Guid.NewGuid(), tenant, board, personalOwner,
                    ownReminder.Status == "SCHEDULED" ? "REMINDER_SCHEDULED" : "REMINDER_CANCELLED", "Reminder", ownReminder.Id,
                    ownReminder.Version, "private-feed-owner", at.AddSeconds(4));
                ownPrivate.Add(watchSource.EventId); ownPrivate.Add(reminderSource.EventId);
                await events.AppendAsync(watchSource, ct); await events.AppendAsync(reminderSource, ct);
                for (var index = 0; index < 100; index++)
                {
                    var change = new WorkEvent(Guid.NewGuid(), tenant, board, actor, "WATCH_CREATED", "WatchSubscription", hiddenWatch.Id,
                        hiddenWatch.Version, "private-feed-hidden", at.AddSeconds(5));
                    hiddenPrivate.Add(change.EventId); await events.AppendAsync(change, ct);
                }
                return true;
            });
            var personalHistory = await Scope(() => feed.ReadAsync(cardBinding, null, ct));
            Require(personalHistory.Count == 51 && personalHistory.Take(2).All(row => ownPrivate.Contains(row.EventId)) &&
                personalHistory.All(row => !hiddenPrivate.Contains(row.EventId)), "Private Card history filtered after its window or followed the source actor.");
            var personalAnchor = personalHistory[49];
            var personalTail = await Scope(() => feed.ReadAsync(cardBinding, new(personalAnchor.CreatedAt, personalAnchor.EventId), ct));
            Require(personalTail.Count == 18 && personalHistory.Take(50).Concat(personalTail).Select(row => row.EventId).Distinct().Count() == 68,
                "Private history displaced, truncated or duplicated shared Card sources.");
            var boardPersonal = await Scope(() => feed.ReadAsync(binding, null, ct));
            Require(boardPersonal.Count == 51 && boardPersonal.Take(2).All(row => ownPrivate.Contains(row.EventId)) &&
                boardPersonal.All(row => !hiddenPrivate.Contains(row.EventId)), "Board activity window disclosed another person's private history.");
            await using (var elevate = new NpgsqlCommand("UPDATE organization_members SET role='ADMIN' WHERE tenant_id=@tenant AND user_id=@actor;", admin))
            {
                elevate.Parameters.AddWithValue("tenant", tenant); elevate.Parameters.AddWithValue("actor", actor); await elevate.ExecuteNonQueryAsync(ct);
            }
            Require((await Scope(() => feed.ReadAsync(cardBinding, null, ct))).SequenceEqual(personalHistory),
                "Organization administration widened private activity ownership.");
            Console.WriteLine("Real restricted activity feeds: complete historical Card seek, source/current Board revocation, own Watch/Reminder audience distinct from actor, 100 hidden personal events filtered before the bound, and no administrator private-history widening passed.");
            Console.WriteLine("Real restricted activity source adapter: bounded same-time seek, immutable captions after rename/deactivation, pending source visibility, exact retry, owning rollback and recovery passed. Raw sources do not establish HTTP/feed audience admission.");
            Console.WriteLine("Real restricted Card activity source slices: bounded tied seek, entity-type/identity/source-Board isolation and explicit historical Board reads passed. Synthetic source setup does not prove actual Card movement or authorized feeds.");
        }
        finally
        {
            await using var cleanupTransaction = await admin.BeginTransactionAsync(ct);
            await using var cleanup = new NpgsqlCommand("""
                UPDATE users SET display_name=@caption,status=@status WHERE id=@actor;
                UPDATE organization_members SET role='MEMBER' WHERE tenant_id=@tenant AND user_id=@actor;
                DELETE FROM background_jobs WHERE tenant_id=@tenant AND safe_metadata->>'boardId'=ANY(@board_texts);
                -- Administrative cleanup of explicitly synthetic history, not a
                -- runtime retention path. The DDL/data reset is one transaction.
                ALTER TABLE organization_board_events DISABLE TRIGGER organization_board_event_history;
                DELETE FROM organization_board_events j USING work_events e
                  WHERE j.tenant_id=e.tenant_id AND j.event_id=e.event_id AND e.tenant_id=@tenant AND e.board_id=ANY(@boards);
                ALTER TABLE organization_board_events ENABLE TRIGGER organization_board_event_history;
                UPDATE organization_board_event_streams s SET last_sequence=COALESCE(
                  (SELECT max(j.sequence) FROM organization_board_events j WHERE j.tenant_id=s.tenant_id),0) WHERE s.tenant_id=@tenant;
                DELETE FROM work_events WHERE tenant_id=@tenant AND board_id=ANY(@boards);
                DELETE FROM work_event_streams WHERE tenant_id=@tenant AND board_id=ANY(@boards);
                DELETE FROM watch_subscriptions WHERE tenant_id=@tenant AND card_id=ANY(@boards);
                DELETE FROM card_reminders WHERE tenant_id=@tenant AND card_id=ANY(@boards);
                DELETE FROM cards WHERE tenant_id=@tenant AND board_id=ANY(@boards);
                DELETE FROM board_lists WHERE tenant_id=@tenant AND board_id=ANY(@boards);
                DELETE FROM boards WHERE tenant_id=@tenant AND id=ANY(@boards);
                DELETE FROM organization_members WHERE tenant_id=@tenant AND user_id=@personal_owner;
                DELETE FROM users WHERE id=@personal_owner;
                """, admin, cleanupTransaction);
            cleanup.Parameters.AddWithValue("caption", originalCaption); cleanup.Parameters.AddWithValue("status", originalStatus);
            cleanup.Parameters.AddWithValue("actor", actor); cleanup.Parameters.AddWithValue("tenant", tenant);
            cleanup.Parameters.AddWithValue("personal_owner", personalOwner);
            cleanup.Parameters.AddWithValue("boards", new[] { board, otherBoard });
            cleanup.Parameters.AddWithValue("board_texts", new[] { board.ToString("D"), otherBoard.ToString("D") });
            await cleanup.ExecuteNonQueryAsync(ct);
            await cleanupTransaction.CommitAsync(ct);
        }
    }
}
