using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CardMentionMemberStoreTests
{
    private sealed class CommandContext : IWorkCommandContext
    { public Guid? IdempotencyKey => null; }
    private sealed class Actor : ICommandActorAuthorization
    {
        public bool Allowed = true;
        public Func<bool>? Probe;
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken ct = default) => Task.FromResult(Probe?.Invoke() ?? Allowed);
    }

    [Fact]
    public async Task PRD_15_CurrentBoardParticipantsSeekAcrossMembershipPagesWithoutGlobalAliasOrTenantDisclosure()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection(); var runtime = new RuntimeDescriptor(RuntimeMode.Demo, "test", "test");
        services.AddSingleton<IClock, SystemClock>(); services.AddSingleton<ICommandActorAuthorization, Actor>();
        services.AddSingleton<IWorkCommandContext, CommandContext>();
        services.AddStrataAiIdentity(new ConfigurationBuilder().Build(), runtime);
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        using var provider = services.BuildServiceProvider();
        var identity = provider.GetRequiredService<IIdentityStore>(); var orgs = provider.GetRequiredService<IOrganizationStore>();
        var work = provider.GetRequiredService<IWorkManagementStore>(); var store = provider.GetRequiredService<ICardMentionMemberStore>();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>(); var identityUnit = provider.GetRequiredService<IIdentityUnitOfWork>();
        var handles = provider.GetRequiredService<IUserMentionHandleStore>(); var at = DateTimeOffset.UtcNow;
        var org = Guid.NewGuid(); var foreign = Guid.NewGuid(); var users = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < users.Length; index++)
        {
            var id = users[index]; var email = $"{id:N}@example.test";
            Assert.True(await identity.TryCreateUserAsync(new(id, email, email.ToUpperInvariant(), "Same name", null, "en", "UTC",
                AccountStatus.Active, index != 59, "fixture", at, at, 1), null, null, ct));
            Assert.True((await identityUnit.ExecuteAsync(id, () => handles.ClaimAsync(id, $"member_{index:D2}", 1, at.AddSeconds(1), ct), ct)).Succeeded);
        }
        await orgs.CreateOrganizationAsync(users[0], org, "Own", null, at, ct);
        await orgs.CreateOrganizationAsync(users[0], foreign, "Foreign", null, at, ct);
        var board = await work.CreateBoardAsync(org, users[0], Guid.NewGuid(), "Board", null, BoardVisibility.Private, "COLOR", null, at, ct);
        var other = await work.CreateBoardAsync(foreign, users[0], Guid.NewGuid(), "Other", null, BoardVisibility.Private, "COLOR", null, at, ct);
        for (var index = 1; index < users.Length; index++)
        {
            await orgs.AddOrRestoreMemberAsync(org, users[index], OrganizationRole.Member, at, ct);
            await work.UpsertBoardMemberAsync(board.Id, users[index], BoardRole.Member, at, ct);
        }
        async Task<T> Scoped<T>(Guid tenant, Func<Task<T>> action)
        {
            // Synthetic fixture admission; actual adapter ownership, not HTTP authorization.
            var result = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true),
                async () => WorkOperation<T>.Success(await action()), ct);
            Assert.True(result.Succeeded); return result.Value!;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SearchAsync(org, board.Id, "member_", null, true, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ResolveAsync(org, board.Id, ["member_00"], true, ct));
        var all = new List<CardMentionMember>(); string? cursor = null;
        do
        {
            var page = await Scoped(org, () => store.SearchAsync(org, board.Id, "member_", cursor, true, ct));
            Assert.InRange(page.Count, 0, 21); all.AddRange(page.Take(20));
            cursor = page.Count == 21 ? page[19].Handle : null;
        } while (cursor is not null);
        Assert.Equal(Enumerable.Range(0, 59).Select(i => $"member_{i:D2}"), all.Select(x => x.Handle));
        Assert.Equal(59, all.Select(x => x.UserId).Distinct().Count()); Assert.All(all, x => Assert.Equal(2, x.HandleVersion));
        Assert.Empty(await Scoped(org, () => store.ResolveAsync(org, other.Id, ["member_00"], true, ct)));
        Assert.Empty(await Scoped(foreign, () => store.ResolveAsync(foreign, board.Id, ["member_00"], true, ct)));
        Assert.Single(await Scoped(foreign, () => store.ResolveAsync(foreign, other.Id, ["member_00", "member_00"], true, ct)));
        Assert.Empty(await Scoped(org, () => store.ResolveAsync(org, board.Id, ["member_59"], true, ct)));
        Assert.Single(await Scoped(org, () => store.ResolveAsync(org, board.Id, ["member_59"], false, ct)));
        Assert.True((await identityUnit.ExecuteAsync(users[0], () => handles.ClaimAsync(users[0], "renamed_member", 2, at.AddSeconds(2), ct), ct)).Succeeded);
        Assert.Empty(await Scoped(org, () => store.ResolveAsync(org, board.Id, ["member_00", $"u_{users[0]:N}"], true, ct)));
        Assert.Equal(3, Assert.Single(await Scoped(org, () => store.ResolveAsync(org, board.Id, ["renamed_member"], true, ct))).HandleVersion);
        await work.RemoveBoardMemberAsync(board.Id, users[1], at.AddSeconds(3), ct);
        await orgs.RemoveMemberAsync(org, users[2], at.AddSeconds(3), ct);
        Assert.True(await identity.DeactivateUserAsync(users[3], at.AddSeconds(3), ct));
        Assert.Empty(await Scoped(org, () => store.ResolveAsync(org, board.Id, ["member_01", "member_02", "member_03"], true, ct)));

        var planning = provider.GetRequiredService<CardCommentMentionPlanning>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => planning.ResolveAsync(org, board.Id, users[0], "@member_04", [], ct));
        var planned = await Scoped(org, () => planning.ResolveAsync(org, board.Id, users[0],
            "  @renamed_member @member_04 @member_05 @member_05 @member_00 @member_01 @member_02 @member_03 @card @board  ", [users[4]], ct));
        Assert.True(planned.Succeeded); Assert.Equal(4, planned.Value!.Recipients.References.Count);
        Assert.Equal(new[] { users[0], users[4], users[5] }.Order(), planned.Value.Recipients.Current);
        Assert.Equal(users[5], Assert.Single(planned.Value.Recipients.Added));
        Assert.True(planned.Value.HasCardMention); Assert.True(planned.Value.HasBoardMention);
        Assert.Equal(planned.Value.Content.Trim(), planned.Value.Content);
        var excess = string.Join(' ', Enumerable.Range(4, 21).Select(i => $"@member_{i:D2}"));
        Assert.Equal("invalid_comment_mentions", (await Scoped(org, () => planning.ResolveAsync(org, board.Id, users[0], excess, [], ct))).ErrorCode);
        Assert.Empty((await Scoped(org, () => planning.ResolveAsync(org, board.Id, users[0], "@member_04", [users[4]], ct))).Value!.Recipients.Added);

        var list = await work.CreateListAsync(board.Id, Guid.NewGuid(), "List", null, at, ct);
        var card = await work.CreateCardAsync(list.Id, Guid.NewGuid(), "Card", null, null, at, ct);
        var options = provider.GetRequiredService<CardMentionOptionsService>();
        var first = await options.ListAsync(card.Id, users[0], " MEMBER_ ", null, ct);
        Assert.True(first.Succeeded); Assert.Equal("member_", first.Value!.Prefix); Assert.Equal(20, first.Value.Items.Count);
        Assert.NotNull(first.Value.NextCursor); Assert.Equal(card.Id, first.Value.CardId);
        var next = await options.ListAsync(card.Id, users[0], "member_", first.Value.NextCursor, ct);
        Assert.True(next.Succeeded); Assert.Empty(first.Value.Items.Select(x => x.UserId).Intersect(next.Value!.Items.Select(x => x.UserId)));
        Assert.Equal("invalid_mention_cursor", (await options.ListAsync(card.Id, users[0], "renamed_", first.Value.NextCursor, ct)).ErrorCode);
        Assert.Equal("invalid_mention_cursor", (await options.ListAsync(card.Id, users[0], "member_", new string('x', 161), ct)).ErrorCode);
        Assert.Equal("mention_prefix_invalid", (await options.ListAsync(card.Id, users[0], "@member", null, ct)).ErrorCode);
        // Organization governance does not confer current Board participation.
        await orgs.AddOrRestoreMemberAsync(org, users[1], OrganizationRole.Admin, at.AddSeconds(4), ct);
        Assert.Equal("card_not_found", (await options.ListAsync(card.Id, users[1], "member_", null, ct)).ErrorCode);
        var actor = (Actor)provider.GetRequiredService<ICommandActorAuthorization>(); actor.Allowed = false;
        Assert.Equal("session_unavailable", (await options.ListAsync(card.Id, users[0], "member_", null, ct)).ErrorCode);
        actor.Allowed = true; var checks = 0; actor.Probe = () => ++checks == 1;
        Assert.Equal("session_unavailable", (await options.ListAsync(card.Id, users[0], "member_", null, ct)).ErrorCode);
        Assert.Equal(2, checks); actor.Probe = null;
        Assert.NotNull(await work.UpdateCardAsync(card.Id, "Changed", null, 1, at.AddSeconds(4), ct));
        Assert.Equal("version_conflict", (await options.ListAsync(card.Id, users[0], "member_", first.Value.NextCursor, ct)).ErrorCode);
        Assert.NotNull(await work.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 2, at.AddSeconds(5), ct));
        Assert.Equal("card_not_found", (await options.ListAsync(card.Id, users[0], "member_", null, ct)).ErrorCode);
    }
}
