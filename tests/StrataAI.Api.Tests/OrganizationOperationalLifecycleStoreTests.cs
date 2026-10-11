using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_blocks_new_board_list_card_activity_without_changing_retained_graph()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Retained operational graph", ct);
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards",
            new { organizationId = parent.Id, name = "Original Board", visibility = "ORGANIZATION" });
        Assert.Equal(HttpStatusCode.Created, boardResponse.StatusCode);
        var boardId = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{boardId}/lists", new { name = "Original List" });
        Assert.Equal(HttpStatusCode.Created, listResponse.StatusCode);
        var listId = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardResponse = await Mutate(owner, HttpMethod.Post, $"/lists/{listId}/cards", new { title = "Original Card", description = "Retained private record" });
        Assert.Equal(HttpStatusCode.Created, cardResponse.StatusCode);
        var cardId = (await cardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var board = await work.FindBoardAsync(boardId, ct); var list = await work.FindListAsync(listId, ct); var card = await work.FindCardAsync(cardId, ct);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var reviewed = (await organizations.FindOrganizationAsync(parent.Id, ct))!;
        var membership = await organizations.FindMembershipAsync(parent.Id, actor, ct);
        var suspended = await app.Services.GetRequiredService<OrganizationOperationalLifecycleService>().ChangeAsync(parent.Id, actor,
            OrganizationStatus.Suspended, reviewed.Version, Guid.NewGuid(), "suspend-retained-graph", ct);
        Assert.True(suspended.Succeeded, suspended.ErrorCode);
        using var blockedBoard = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = parent.Id, name = "Blocked Board" });
        using var blockedList = await Mutate(owner, HttpMethod.Post, $"/boards/{boardId}/lists", new { name = "Blocked List" });
        using var blockedCard = await Mutate(owner, HttpMethod.Post, $"/lists/{listId}/cards", new { title = "Blocked Card" });
        using var blockedEdit = await Mutate(owner, HttpMethod.Patch, $"/cards/{cardId}",
            new { title = "Blocked edit", description = "Blocked content", version = card!.Version });
        foreach (var response in new[] { blockedBoard, blockedList, blockedCard, blockedEdit })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain("Original Card", body); Assert.DoesNotContain("Retained private record", body);
        }
        Assert.Equal(board, await work.FindBoardAsync(boardId, ct)); Assert.Equal(list, await work.FindListAsync(listId, ct));
        Assert.Equal(card, await work.FindCardAsync(cardId, ct)); Assert.Equal(membership, await organizations.FindMembershipAsync(parent.Id, actor, ct));
        var reactivated = await app.Services.GetRequiredService<OrganizationOperationalLifecycleService>().ChangeAsync(parent.Id, actor,
            OrganizationStatus.Active, suspended.Value!.Version, Guid.NewGuid(), "reactivate-retained-graph", ct);
        Assert.True(reactivated.Succeeded, reactivated.ErrorCode);
        using var resumed = await Mutate(owner, HttpMethod.Post, $"/lists/{listId}/cards", new { title = "New permitted Card" });
        Assert.Equal(HttpStatusCode.Created, resumed.StatusCode);
        Assert.Equal(card, await work.FindCardAsync(cardId, ct));
    }

    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_persists_status_receipt_audit_and_preserves_configuration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Retained lifecycle parent", ct);
        var configurations = app.Services.GetRequiredService<OrganizationConfigurationService>();
        var original = await configurations.ChangeAsync(parent.Id, actor, new("Original legal name", "CA-BC", "UTC"),
            0, Guid.NewGuid(), "original-configuration", ct); Assert.True(original.Succeeded, original.ErrorCode);
        var service = app.Services.GetRequiredService<OrganizationOperationalLifecycleService>();
        var key = Guid.NewGuid();
        var changed = await service.ChangeAsync(parent.Id, actor, OrganizationStatus.Suspended, parent.Version, key,
            new string('x', 256), ct); Assert.True(changed.Succeeded, changed.ErrorCode);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var saved = await organizations.FindOrganizationAsync(parent.Id, ct);
        Assert.Equal(OrganizationStatus.Suspended, saved!.Status); Assert.Equal(parent.Version + 1, saved.Version);
        Assert.Equal(parent.Name, saved.Name); Assert.Equal(parent.CreatedAt, saved.CreatedAt); Assert.Equal(parent.Type, saved.Type);
        Assert.Equal(changed.Value!.CreatedAt, saved.UpdatedAt);
        var audits = app.Services.GetRequiredService<InMemoryWorkManagementStore>().AuditSnapshot(parent.Id);
        var source = Assert.Single(audits, a => a.EventType == "ORGANIZATION_SUSPENDED");
        Assert.Equal(changed.Value.EventId, source.Id); Assert.Equal(changed.Value.CreatedAt, source.CreatedAt);
        var denied = await configurations.ChangeAsync(parent.Id, actor, original.Value!.Configuration with { LegalName = "Blocked edit" },
            1, Guid.NewGuid(), "blocked-configuration", ct); Assert.Equal("organization_not_found", denied.ErrorCode);
        Assert.Equal("organization_not_found", (await configurations.ReadHistoryAsync(parent.Id, actor, null, ct)).ErrorCode);
        Assert.Equal("organization_not_found", (await app.Services.GetRequiredService<IOrganizationService>().ReadAsync(parent.Id, actor, ct)).ErrorCode);
        var recovered = await service.ChangeAsync(parent.Id, actor, OrganizationStatus.Suspended, parent.Version, key, "retry", ct);
        Assert.True(recovered.Succeeded); Assert.Equal(changed.Value, recovered.Value);
        Assert.Single(app.Services.GetRequiredService<InMemoryWorkManagementStore>().AuditSnapshot(parent.Id),
            a => a.EventType == "ORGANIZATION_SUSPENDED");
        var reactivated = await service.ChangeAsync(parent.Id, actor, OrganizationStatus.Active, saved.Version, Guid.NewGuid(), "reactivate", ct);
        Assert.True(reactivated.Succeeded, reactivated.ErrorCode);
        var history = await configurations.ReadHistoryAsync(parent.Id, actor, null, ct); Assert.True(history.Succeeded);
        Assert.Equal(JsonSerializer.Serialize(original.Value), JsonSerializer.Serialize(Assert.Single(history.Value!.Items)));
        Assert.True((await service.ChangeAsync(parent.Id, actor, OrganizationStatus.Suspended, parent.Version, key, "old-retry", ct)).Succeeded);
        Assert.Equal(OrganizationStatus.Active, (await organizations.FindOrganizationAsync(parent.Id, ct))!.Status);
    }

    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_failed_owning_transaction_restores_parent_audit_and_receipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Rollback lifecycle parent", ct);
        var store = app.Services.GetRequiredService<IOrganizationOperationalLifecycleStore>();
        var unit = app.Services.GetRequiredService<IOrganizationOperationalLifecycleUnitOfWork>();
        var clock = app.Services.GetRequiredService<IClock>(); var key = Guid.NewGuid();
        Guid? eventId = null;
        var aborted = await unit.ExecuteAsync<bool>(parent.Id, actor, async () =>
        {
            var scope = (await store.ReadScopeAsync(parent.Id, actor, ct))!;
            var at = clock.UtcNow; at = at.AddTicks(-(at.Ticks % 10));
            var source = new OrganizationOperationalLifecycleTransition(parent.Id, Guid.NewGuid(), actor, scope.ActorLabel,
                "ORGANIZATION_OWNER", scope.MembershipId, scope.MembershipVersion, scope.Status, OrganizationStatus.Deactivated,
                scope.Version + 1, "rollback-source", at); eventId = source.EventId;
            var receipt = new OrganizationOperationalLifecycleReceipt(actor, key,
                OrganizationOperationalLifecycleRules.Fingerprint(parent.Id, source.Status, scope.Version), source, at.AddHours(24));
            Assert.Equal(OrganizationOperationalLifecycleWriteResult.Written, await store.WriteAsync(scope.Version, source, receipt, ct));
            return OrganizationOperation<bool>.Failure("injected_after_source_failure");
        }, ct);
        Assert.False(aborted.Succeeded);
        Assert.Equal(parent, await app.Services.GetRequiredService<IOrganizationStore>().FindOrganizationAsync(parent.Id, ct));
        Assert.DoesNotContain(app.Services.GetRequiredService<InMemoryWorkManagementStore>().AuditSnapshot(parent.Id), a => a.Id == eventId);
        var checkedReceipt = await unit.ExecuteAsync<bool>(parent.Id, actor, async () =>
        { Assert.Null(await store.ReadReceiptAsync(parent.Id, actor, key, ct)); return OrganizationOperation<bool>.Success(true); }, ct);
        Assert.True(checkedReceipt.Succeeded);
        var changed = await app.Services.GetRequiredService<OrganizationOperationalLifecycleService>().ChangeAsync(parent.Id, actor,
            OrganizationStatus.Deactivated, parent.Version, key, "corrected-review", ct);
        Assert.True(changed.Succeeded, changed.ErrorCode);
    }

    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_rejects_unowned_and_generic_organization_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Narrow lifecycle parent", ct);
        var store = app.Services.GetRequiredService<IOrganizationOperationalLifecycleStore>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadScopeAsync(parent.Id, actor, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Services.GetRequiredService<IOrganizationUnitOfWork>()
            .ExecuteAsync<bool>(parent.Id, actor, null, false, async () =>
            { await store.ReadScopeAsync(parent.Id, actor, ct); return OrganizationOperation<bool>.Success(true); }, ct));
        Assert.Equal(parent, await app.Services.GetRequiredService<IOrganizationStore>().FindOrganizationAsync(parent.Id, ct));
        var unit = app.Services.GetRequiredService<IOrganizationOperationalLifecycleUnitOfWork>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync<bool>(parent.Id, actor, async () =>
        { await store.ReadScopeAsync(parent.Id, Guid.NewGuid(), ct); return OrganizationOperation<bool>.Success(true); }, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync<bool>(parent.Id, actor, async () =>
        { await store.ReadReceiptAsync(Guid.NewGuid(), actor, Guid.NewGuid(), ct); return OrganizationOperation<bool>.Success(true); }, ct));
    }

    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_ordinary_member_and_foreign_actor_do_not_change_parent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var stranger = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(stranger);
        var (actor, parent) = await ConfigurationParent(owner, "Private lifecycle parent", ct);
        var strangerId = (await stranger.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var service = app.Services.GetRequiredService<OrganizationOperationalLifecycleService>();
        Assert.Equal("organization_not_found", (await service.ChangeAsync(parent.Id, strangerId, OrganizationStatus.Suspended,
            parent.Version, Guid.NewGuid(), "guessed-organization", ct)).ErrorCode);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var added = await app.Services.GetRequiredService<IOrganizationUnitOfWork>().ExecuteAsync<bool>(parent.Id, actor, strangerId, false,
            async () => { await organizations.AddOrRestoreMemberAsync(parent.Id, strangerId, OrganizationRole.Member,
                app.Services.GetRequiredService<IClock>().UtcNow, ct); return OrganizationOperation<bool>.Success(true); }, ct);
        Assert.True(added.Succeeded);
        Assert.Equal("organization_not_found", (await service.ChangeAsync(parent.Id, strangerId, OrganizationStatus.Suspended,
            parent.Version, Guid.NewGuid(), "ordinary-member", ct)).ErrorCode);
        Assert.Equal(parent, await organizations.FindOrganizationAsync(parent.Id, ct));
        Assert.DoesNotContain(app.Services.GetRequiredService<InMemoryWorkManagementStore>().AuditSnapshot(parent.Id),
            a => a.EventType == "ORGANIZATION_SUSPENDED");
    }
}
