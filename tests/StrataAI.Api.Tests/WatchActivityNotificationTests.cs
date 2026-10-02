using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Watch_activity_is_transactional_personal_deduplicated_and_uses_destination_scope_with_assignment_precedence()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        using var sourceOnly = app.CreateClient(); using var destinationOnly = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var notifications = app.Services.GetRequiredService<IWorkNotificationStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Watched activity", null, null, DateTimeOffset.UtcNow, ct);
        var destination = await work.CreateListAsync(f.Board, Guid.NewGuid(), "Destination", null, DateTimeOffset.UtcNow, ct);
        async Task<Guid> Admit(HttpClient client)
        {
            await RegisterAndLogin(client); var user = (await client.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
            await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(f.Organization, user, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
            using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{user}", new { role = "MEMBER" });
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode); return user;
        }
        var sourceUser = await Admit(sourceOnly); var destinationUser = await Admit(destinationOnly);
        async Task Watch(HttpClient client, string type, Guid id)
        {
            using var response = await Mutate(client, HttpMethod.Put, $"/watch/{type}/{id}?version=0", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await Watch(recipient, "CARD", card.Id); await Watch(recipient, "LIST", f.List); await Watch(recipient, "BOARD", f.Board);
        await Watch(sourceOnly, "LIST", f.List); await Watch(destinationOnly, "LIST", destination.Id); await Watch(owner, "BOARD", f.Board);
        Assert.Empty(await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "New watched Card" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("CARD_CREATED", Assert.Single(await notifications.ListCardNotificationsAsync(f.Organization, sourceUser, cancellationToken: ct)).NotificationType);
        Assert.Empty(await notifications.ListCardNotificationsAsync(f.Organization, destinationUser, cancellationToken: ct));
        var moveKey = Guid.NewGuid().ToString();
        using var moved = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/move", new { destinationListId = destination.Id, expectedVersion = 1 }, moveKey);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var movedNotification = Assert.Single(await notifications.ListCardNotificationsAsync(f.Organization, destinationUser, cancellationToken: ct));
        Assert.Equal("CARD_MOVED", movedNotification.NotificationType); Assert.Equal(card.Id, movedNotification.CardId);
        Assert.Single(await notifications.ListCardNotificationsAsync(f.Organization, sourceUser, cancellationToken: ct));
        using var replay = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/move", new { destinationListId = destination.Id, expectedVersion = 1 }, moveKey);
        Assert.Equal(await moved.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(movedNotification, Assert.Single(await notifications.ListCardNotificationsAsync(f.Organization, destinationUser, cancellationToken: ct)));
        using var assigned = await Mutate(owner, HttpMethod.Put, $"/cards/{card.Id}/members/{f.Recipient}?version=2", new { });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var personal = await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct);
        Assert.Equal(3, personal.Count); Assert.Single(personal, n => n.NotificationType == "CARD_ASSIGNED");
        Assert.DoesNotContain(personal, n => n.NotificationType == "CARD_MEMBER_ADDED");
        Assert.Empty(await notifications.ListCardNotificationsAsync(f.Organization, f.Owner, cancellationToken: ct));
        var inbox = await recipient.GetFromJsonAsync<NotificationInboxPage>($"/organizations/{f.Organization}/notifications", ct);
        Assert.NotNull(inbox); Assert.Equal(3, inbox.Items.Count);
        Assert.Contains(inbox.Items, n => n.Type == "CARD_MOVED" && n.EntityId == card.Id);
        await work.RemoveBoardMemberAsync(f.Board, destinationUser, DateTimeOffset.UtcNow, ct);
        var before = (await notifications.ListCardNotificationsAsync(f.Organization, destinationUser, cancellationToken: ct)).Count;
        using var unassigned = await Mutate(owner, HttpMethod.Delete, $"/cards/{card.Id}/members/{f.Recipient}?version=3", new { });
        Assert.Equal(HttpStatusCode.OK, unassigned.StatusCode);
        Assert.Equal(before, (await notifications.ListCardNotificationsAsync(f.Organization, destinationUser, cancellationToken: ct)).Count);
        var hidden = await destinationOnly.GetFromJsonAsync<NotificationInboxPage>($"/organizations/{f.Organization}/notifications", ct);
        Assert.NotNull(hidden); Assert.Empty(hidden.Items);
    }
}
