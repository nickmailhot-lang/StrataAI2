using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Organization_deletion_suspends_choices_across_all_52_boards_without_changing_cancelled_or_other_tenant_choices()
    {
        var ct = TestContext.Current.CancellationToken; var publisher = new ReminderPublisher();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICardReminderJobPublisher>(publisher));
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        var dates = app.Services.GetRequiredService<ICardDateStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var now = DateTimeOffset.UtcNow; var chosen = new List<(CardRecord Card, CardReminder Reminder)>();
        async Task<(CardRecord Card, CardReminder Reminder)> Choice(Guid tenant)
        {
            var board = await work.CreateBoardAsync(tenant, f.Owner, Guid.NewGuid(), "Lifecycle Board", null,
                BoardVisibility.Private, "color", null, now, ct);
            var list = await work.CreateListAsync(board.Id, Guid.NewGuid(), "Lifecycle List", null, now, ct);
            var card = await work.CreateCardAsync(list.Id, Guid.NewGuid(), "Lifecycle Card", null, null, now, ct);
            card = (await dates.SetDatesAsync(tenant, board.Id, card.Id, new(null, now.AddDays(2), "UTC", true, false), 1, now, ct))!;
            var reminder = (await reminders.SetAsync(card, f.Owner, "AT_DUE", true, 0, now, ct))!;
            return (card, reminder);
        }
        for (var index = 0; index < 52; index++) chosen.Add(await Choice(f.Organization));
        var cancelled = (await reminders.SetAsync(chosen[0].Card, f.Recipient, "1_HOUR", false, 0, now, ct))!;
        var foreignOrg = await organizations.CreateOrganizationAsync(f.Owner, Guid.NewGuid(), "Other tenant", null, now, ct);
        var foreign = await Choice(foreignOrg.Id);
        using var denied = await Mutate(member, HttpMethod.Delete, $"/organizations/{f.Organization}?version=1", new { });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Delete, $"/organizations/{f.Organization}?version=99", new { });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        foreach (var entry in chosen) Assert.Equal(entry.Reminder, await reminders.FindAsync(f.Organization, f.Owner, entry.Card.Id, ct));
        using var deleted = await Mutate(owner, HttpMethod.Delete, $"/organizations/{f.Organization}?version=1", new { });
        Assert.Equal(HttpStatusCode.Accepted, deleted.StatusCode);
        var organization = await organizations.FindOrganizationAsync(f.Organization, ct);
        Assert.Equal(OrganizationStatus.Deleting, organization!.Status); Assert.Equal(2, organization.Version);
        foreach (var entry in chosen)
        {
            var current = (await reminders.FindAsync(f.Organization, f.Owner, entry.Card.Id, ct))!;
            Assert.Equal(entry.Reminder.Id, current.Id); Assert.True(current.Enabled);
            Assert.Equal("SUSPENDED", current.Status); Assert.Null(current.TriggerAt);
            Assert.Equal(entry.Reminder.Generation + 1, current.Generation); Assert.Equal(entry.Reminder.Version + 1, current.Version);
            Assert.Equal(entry.Card, await work.FindCardAsync(entry.Card.Id, ct));
        }
        Assert.Equal(cancelled, await reminders.FindAsync(f.Organization, f.Recipient, chosen[0].Card.Id, ct));
        Assert.Equal(foreign.Reminder, await reminders.FindAsync(foreignOrg.Id, f.Owner, foreign.Card.Id, ct));
        Assert.Equal(foreign.Card, await work.FindCardAsync(foreign.Card.Id, ct)); Assert.Empty(publisher.Jobs);
    }
}
