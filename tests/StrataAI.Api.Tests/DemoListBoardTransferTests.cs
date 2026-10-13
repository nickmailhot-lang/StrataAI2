using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using StrataAI.Infrastructure.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    public static bool PostMvpLoadEnabled => Environment.GetEnvironmentVariable("STRATAAI_POST_MVP_LOAD_TESTS") == "1";

    [Fact(Skip = "Large-load acceptance is deferred until after the operational MVP.", SkipUnless = nameof(PostMvpLoadEnabled))]
    public async Task PRD_07_Demo_large_archived_graph_keeps_every_identity_and_one_shared_definition()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var organization = (await work.FindBoardAsync(board, ct))!.OrganizationId;
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow; var destination = await work.CreateBoardAsync(organization, actor, Guid.NewGuid(), "Large target", null,
            BoardVisibility.Private, "COLOR", null, now, ct);
        var list = await work.CreateListAsync(board, Guid.NewGuid(), "Large source", RankToken.Initial(), now, ct);
        var label = await work.CreateLabelAsync(board, Guid.NewGuid(), "Shared archive label", "blue", now, ct);
        var scope = app.Services.GetRequiredService<DemoWorkTransactionScope>();
        using (scope.Enter(organization))
        {
            var before = new List<CardRecord>();
            for (var n = 0; n < 100_000; n++)
            {
                var card = await work.CreateCardAsync(list.Id, Guid.NewGuid(), "Archive subject", "Stable body", RankToken.Initial(), now, ct);
                Assert.NotNull(await work.SetCardLabelAsync(card.Id, label.Id, true, 1, now, ct));
                var archived = await work.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 2, now, ct);
                Assert.NotNull(archived); before.Add(archived);
            }
            var result = await app.Services.GetRequiredService<IListBoardMovementStore>().TransferListBoardAsync(organization, board, list.Id,
                destination.Id, 1, now.AddSeconds(1), false, null, ct);
            Assert.NotNull(result); Assert.Equal(before.Count, result.Cards.Count); Assert.Equal(before.Count, result.Cards.Select(c => c.Id).Distinct().Count());
            foreach (var card in before)
                Assert.Equal(card with { BoardId = destination.Id, UpdatedAt = now.AddSeconds(1), Version = card.Version + 1 }, await work.FindCardAsync(card.Id, ct, includeDeleted: true));
            Assert.Single(await work.ListLabelsAsync(destination.Id, null, ct));
        }
    }

    [Fact]
    public async Task PRD_07_Demo_owning_transfer_preserves_all_lifecycles_shared_labels_and_complete_rollback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var organization = (await work.FindBoardAsync(board, ct))!.OrganizationId;
        async Task<Guid> Create(string path, object body)
        {
            using var response = await Mutate(owner, HttpMethod.Post, path, body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        }
        var destination = await Create("/boards", new { organizationId = organization, name = "Destination", visibility = "PRIVATE" });
        var list = await Create($"/boards/{board}/lists", new { name = "All lifecycle children" });
        var anchor = await Create($"/boards/{destination}/lists", new { name = "Anchor" });
        var cards = new List<Guid>(); var label = await Create($"/boards/{board}/labels", new { name = "Shared", color = "blue" });
        for (var n = 0; n < 3; n++)
        {
            var card = await Create($"/lists/{list}/cards", new { title = "Stable " + n, description = "Stable content" }); cards.Add(card);
            using var assigned = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/labels/{label}?version=1", new { });
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
            if (n > 0)
            {
                using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/archive", new { version = 2 }); Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
                if (n == 2) { using var deleted = await Mutate(owner, HttpMethod.Delete, $"/cards/{card}?version=3&confirmed=true", new { }); Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); }
            }
        }
        var source = (await work.FindListAsync(list, ct))!;
        var before = new List<CardRecord>(); foreach (var id in cards) before.Add((await work.FindCardAsync(id, ct, includeDeleted: true))!);
        var movement = Assert.IsAssignableFrom<IListBoardMovementStore>(work);
        await Assert.ThrowsAsync<InvalidOperationException>(() => movement.TransferListBoardAsync(organization, board, list, destination, 1, DateTimeOffset.UtcNow, false, anchor, ct));
        var scope = app.Services.GetRequiredService<DemoWorkTransactionScope>();
        using (scope.Enter(organization))
        {
            var store = Assert.IsType<InMemoryWorkManagementStore>(work); var rollback = store.CaptureRollback();
            var now = DateTimeOffset.UtcNow.AddMinutes(1);
            var bindings = app.Services.GetRequiredService<DemoOrganizationConfigurationState>();
            var hadConfiguration = bindings.Current.TryGetValue(organization, out var originalConfiguration);
            bindings.Current[organization] = JsonSerializer.Serialize(new { Configuration = new { IntakeListId = list } });
            Assert.Null(await movement.TransferListBoardAsync(organization, board, list, destination, 1, now, false, anchor, ct));
            Assert.Equal(source, await work.FindListAsync(list, ct));
            Assert.Empty(await work.ListLabelsAsync(destination, null, ct));
            if (hadConfiguration) bindings.Current[organization] = originalConfiguration!; else bindings.Current.Remove(organization);
            Assert.Null(await movement.TransferListBoardAsync(organization, board, list, destination, 99, now, false, anchor, ct));
            Assert.Null(await movement.TransferListBoardAsync(organization, board, list, destination, 1, now, false, Guid.NewGuid(), ct));
            var result = await movement.TransferListBoardAsync(organization, board, list, destination, 1, now, false, anchor, ct);
            Assert.NotNull(result); Assert.Equal(3, result.Cards.Count); Assert.Equal(2, result.List.Version);
            Assert.Equal(source with { BoardId = destination, Rank = result.List.Rank, UpdatedAt = now, Version = 2 }, result.List);
            Assert.True(string.CompareOrdinal(result.List.Rank, (await work.FindListAsync(anchor, ct))!.Rank) < 0);
            for (var n = 0; n < cards.Count; n++)
                Assert.Equal(before[n] with { BoardId = destination, UpdatedAt = now, Version = before[n].Version + 1 }, await work.FindCardAsync(cards[n], ct, includeDeleted: true));
            var copied = Assert.Single(await work.ListLabelsAsync(destination, null, ct)); Assert.NotEqual(label, copied.Id);
            rollback();
            Assert.Equal(source, await work.FindListAsync(list, ct));
            for (var n = 0; n < cards.Count; n++) Assert.Equal(before[n], await work.FindCardAsync(cards[n], ct, includeDeleted: true));
            Assert.Empty(await work.ListLabelsAsync(destination, null, ct));
            Assert.NotNull(await movement.TransferListBoardAsync(organization, board, list, destination, 1, now, false, anchor, ct));
        }
    }

    [Fact]
    public async Task PRD_07_Demo_binding_reads_the_canonical_current_snapshot_and_observes_rollback()
    {
        await using var app = new ApiFactory(); var ct = TestContext.Current.CancellationToken;
        var scope = app.Services.GetRequiredService<DemoWorkTransactionScope>();
        var binding = app.Services.GetRequiredService<DemoOrganizationConfigurationState>();
        var organization = Guid.NewGuid(); var list = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => binding.IsCurrentIntake(organization, list));
        using (scope.Enter(organization))
        {
            binding.Current[organization] = JsonSerializer.Serialize(new { Configuration = new { IntakeListId = list } });
            Assert.True(binding.IsCurrentIntake(organization, list));
            var store = app.Services.GetRequiredService<InMemoryOrganizationConfigurationStore>(); var rollback = store.CaptureRollback();
            binding.Current.Remove(organization); Assert.False(binding.IsCurrentIntake(organization, list));
            rollback(); Assert.True(binding.IsCurrentIntake(organization, list));
            Assert.Throws<InvalidOperationException>(() => binding.IsCurrentIntake(Guid.NewGuid(), list));
        }
    }
}
