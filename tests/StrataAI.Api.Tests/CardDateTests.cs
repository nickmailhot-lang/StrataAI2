using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Card_dates_roundtrip_replay_noop_completion_clear_and_current_scope_admission()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter<WorkItemLifecycleState>(JsonNamingPolicy.CamelCase));
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var notifications = app.Services.GetRequiredService<IWorkNotificationStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Dates", null, null, DateTimeOffset.UtcNow, ct);
        using var watch = await Mutate(recipient, HttpMethod.Put, $"/watch/CARD/{card.Id}?version=0", new { });
        Assert.Equal(HttpStatusCode.OK, watch.StatusCode);
        var path = $"/cards/{card.Id}/dates"; var key = Guid.NewGuid().ToString();
        var input = new CardDatesInput("2026-03-08", "2026-03-08", "America/Vancouver", false, false, 1);
        using var denied = await Mutate(outsider, HttpMethod.Patch, path, input);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Patch, path, input with { DueTimezone = "Unknown/Place" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(1, (await work.FindCardAsync(card.Id, ct))!.Version);
        using var changed = await Mutate(owner, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var result = await changed.Content.ReadFromJsonAsync<CardDateChange>(json, ct); Assert.NotNull(result); Assert.True(result.Changed);
        Assert.Equal(2, result.Card.Version); Assert.False(result.Card.DueHasTime); Assert.NotNull(result.Card.DueAt);
        Assert.Equal("America/Vancouver", result.Card.DueTimezone);
        using var replay = await Mutate(owner, HttpMethod.Patch, path, input, key);
        Assert.Equal(await changed.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Patch, path, input); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var noop = await Mutate(owner, HttpMethod.Patch, path, input with { Version = 2 });
        var same = await noop.Content.ReadFromJsonAsync<CardDateChange>(json, ct); Assert.NotNull(same); Assert.False(same.Changed); Assert.Equal(2, same.Card.Version);
        Assert.Single(await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        var complete = new CardDatesInput(result.Card.StartAt!.Value.ToString("O"), result.Card.DueAt!.Value.ToString("O"), "America/Vancouver", false, true, 2);
        using var completed = await Mutate(owner, HttpMethod.Patch, path, complete);
        var done = await completed.Content.ReadFromJsonAsync<CardDateChange>(json, ct); Assert.NotNull(done); Assert.True(done.Card.DueComplete);
        Assert.Equal(WorkItemLifecycleState.Active, done.Card.LifecycleState); Assert.Equal(3, done.Card.Version);
        using var reopened = await Mutate(owner, HttpMethod.Patch, path, complete with { DueComplete = false, Version = 3 });
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        using var clear = await Mutate(owner, HttpMethod.Patch, path, new CardDatesInput(null, null, null, false, false, 4));
        var cleared = await clear.Content.ReadFromJsonAsync<CardDateChange>(json, ct); Assert.NotNull(cleared); Assert.Null(cleared.Card.DueAt);
        Assert.Null(cleared.Card.StartAt); Assert.Null(cleared.Card.DueTimezone); Assert.False(cleared.Card.DueComplete); Assert.Equal(5, cleared.Card.Version);
        var inbox = await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct);
        Assert.Equal(4, inbox.Count); Assert.Equal(2, inbox.Count(n => n.NotificationType == "CARD_DATE_CHANGED"));
        Assert.Single(inbox, n => n.NotificationType == "CARD_DUE_COMPLETED"); Assert.Single(inbox, n => n.NotificationType == "CARD_DUE_REOPENED");
        using var archive = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 5 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var hiddenReplay = await Mutate(owner, HttpMethod.Patch, path, input, key); Assert.Equal(HttpStatusCode.NotFound, hiddenReplay.StatusCode);
        using var archiveChange = await Mutate(owner, HttpMethod.Patch, path, input with { Version = 6 }); Assert.Equal(HttpStatusCode.NotFound, archiveChange.StatusCode);
    }
}
