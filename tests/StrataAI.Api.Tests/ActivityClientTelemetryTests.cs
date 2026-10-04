using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("boardId")]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("backgroundValue")]
    [InlineData("selection")]
    [InlineData("version")]
    [InlineData("key")]
    [InlineData("diagnostic")]
    public void PRD_04_Metadata_observations_reject_private_extras_atomically(string field)
    {
        using var valid = JsonDocument.Parse("""
            {"events":[{"action":"board_metadata_update","kind":"open","count":1},
            {"action":"board_metadata_update","kind":"retry","count":1},
            {"action":"board_metadata_update","kind":"success","count":1,"durationMs":125}]}
            """);
        Assert.Equal(3, ActivityClientTelemetry.Parse(valid.RootElement)!.Count);
        using var invalid = JsonDocument.Parse(JsonSerializer.Serialize(new { events = new object[] {
            new { action = "board_metadata_update", kind = "open", count = 1 },
            new Dictionary<string, object> { ["action"] = "board_metadata_update", ["kind"] = "use", ["count"] = 1, [field] = "private-material" } } }));
        Assert.Null(ActivityClientTelemetry.Parse(invalid.RootElement));
    }

    [Theory]
    [InlineData("archive_list_disclosure")]
    [InlineData("archive_list_read")]
    [InlineData("archive_list_restore")]
    [InlineData("archive_list_delete")]
    [InlineData("archive_card_disclosure")]
    [InlineData("archive_card_read")]
    [InlineData("archive_card_restore")]
    [InlineData("archive_card_delete")]
    [InlineData("list_archive")]
    [InlineData("card_archive")]
    [InlineData("archive_board_disclosure")]
    [InlineData("archive_board_read")]
    [InlineData("archive_board_restore")]
    [InlineData("archive_board_delete")]
    [InlineData("board_archive")]
    public void PRD_18_Archive_observations_accept_fixed_actions_and_reject_private_material(string action)
    {
        using var valid = JsonDocument.Parse(JsonSerializer.Serialize(new { events = new object[] {
            new { action, kind = "open", count = 1 }, new { action, kind = "reconnect", count = 1 },
            new { action, kind = "failure", count = 1, durationMs = 125 } } }));
        Assert.Equal(3, ActivityClientTelemetry.Parse(valid.RootElement)!.Count);
        foreach (var field in new[] { "boardId", "cardId", "listId", "name", "key", "containedCardCount", "diagnostic" })
        {
            using var invalid = JsonDocument.Parse(JsonSerializer.Serialize(new { events = new object[] {
                new { action, kind = "open", count = 1 },
                new Dictionary<string, object> { ["action"] = action, ["kind"] = "use", ["count"] = 1, [field] = "private-material" } } }));
            Assert.Null(ActivityClientTelemetry.Parse(invalid.RootElement));
        }
    }

    [Theory]
    [InlineData("subscriptionId")]
    [InlineData("userId")]
    [InlineData("entityId")]
    [InlineData("watching")]
    [InlineData("key")]
    [InlineData("diagnostic")]
    public void PRD_17_Watch_client_observations_accept_fixed_categories_and_reject_private_material(string field)
    {
        using var valid = JsonDocument.Parse("""
            {"events":[{"action":"watch_disclosure","kind":"open","count":1},
            {"action":"watch_read","kind":"reconnect","count":1},
            {"action":"watch_change","kind":"success","count":1,"durationMs":125}]}
            """);
        Assert.Equal(3, ActivityClientTelemetry.Parse(valid.RootElement)!.Count);
        var payload = JsonSerializer.Serialize(new { events = new object[] {
            new { action = "watch_disclosure", kind = "open", count = 1 },
            new Dictionary<string, object> { ["action"] = "watch_change", ["kind"] = "use", ["count"] = 1, [field] = "private-material" } } });
        using var invalid = JsonDocument.Parse(payload);
        Assert.Null(ActivityClientTelemetry.Parse(invalid.RootElement));
    }

    [Theory]
    [InlineData("notificationId")]
    [InlineData("recipientId")]
    [InlineData("entityLink")]
    [InlineData("body")]
    [InlineData("key")]
    [InlineData("after")]
    public void PRD_17_Notification_client_observations_accept_fixed_categories_and_reject_private_material(string field)
    {
        using var valid = JsonDocument.Parse("""
            {"events":[{"action":"notification_disclosure","kind":"open","count":1},
            {"action":"notification_read","kind":"reconnect","count":1},
            {"action":"notification_mark_read","kind":"success","count":1,"durationMs":125}]}
            """);
        Assert.Equal(3, ActivityClientTelemetry.Parse(valid.RootElement)!.Count);
        var payload = JsonSerializer.Serialize(new { events = new object[] {
            new { action = "notification_disclosure", kind = "open", count = 1 },
            new Dictionary<string, object> { ["action"] = "notification_mark_read", ["kind"] = "use", ["count"] = 1, [field] = "private-material" } } });
        using var invalid = JsonDocument.Parse(payload);
        Assert.Null(ActivityClientTelemetry.Parse(invalid.RootElement));
    }

    [Theory]
    [InlineData("q")]
    [InlineData("label")]
    [InlineData("member")]
    [InlineData("after")]
    [InlineData("resultTitle")]
    public void PRD_16_Search_client_observations_admit_fixed_actions_and_reject_private_material(string field)
    {
        using var valid = JsonDocument.Parse("""
            {"events":[{"action":"search_disclosure","kind":"open","count":1},
            {"action":"search_read","kind":"reconnect","count":1},
            {"action":"search_read","kind":"success","count":1,"durationMs":125}]}
            """);
        Assert.Equal(3, ActivityClientTelemetry.Parse(valid.RootElement)!.Count);
        var payload = JsonSerializer.Serialize(new { events = new object[] {
            new { action = "search_disclosure", kind = "open", count = 1 },
            new Dictionary<string, object> { ["action"] = "search_read", ["kind"] = "use", ["count"] = 1, [field] = "private-material" } } });
        using var invalid = JsonDocument.Parse(payload);
        Assert.Null(ActivityClientTelemetry.Parse(invalid.RootElement));
    }

    [Fact]
    public async Task Activity_client_observations_are_authenticated_bounded_and_reject_private_batches_atomically()
    {
        await using var app = new ApiFactory(); using var actor = app.CreateClient(); using var anonymous = app.CreateClient();
        await RegisterAndLogin(actor);
        var meter = app.Services.GetRequiredService<ActivityClientTelemetry>().Meter;
        var observations = new ConcurrentQueue<(long Value, Dictionary<string, object?> Tags)>();
        var durations = new ConcurrentQueue<double>(); using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) => { if (ReferenceEquals(instrument.Meter, meter)) current.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => observations.Enqueue((value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => durations.Enqueue(value)); listener.Start();
        var payload = new { events = new object[] { new { action = "card_disclosure", kind = "open", count = 2 },
            new { action = "board_read", kind = "retry", count = 1 }, new { action = "card_read", kind = "success", count = 1, durationMs = 125d },
            new { action = "comment_create", kind = "use", count = 1 }, new { action = "comment_create", kind = "success", count = 1, durationMs = 125d },
            new { action = "mention_selection", kind = "use", count = 1 }, new { action = "board_group_confirmation", kind = "use", count = 1 } } };
        using var denied = await Mutate(anonymous, HttpMethod.Post, "/me/activity-client-events", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Empty(observations);
        using var accepted = await Mutate(actor, HttpMethod.Post, "/me/activity-client-events", payload);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode); Assert.Equal(7, observations.Count);
        Assert.Equal(8, observations.Sum(item => item.Value)); Assert.Equal(2, durations.Count); Assert.All(durations, value => Assert.Equal(.125, value));
        Assert.All(observations, item => Assert.Equal(new[] { "action", "kind" }, item.Tags.Keys.Order().ToArray()));
        foreach (var body in new[] {
            "{\"events\":[{\"action\":\"card_read\",\"kind\":\"use\",\"count\":1},{\"action\":\"card_read\",\"kind\":\"use\",\"count\":1,\"cursor\":\"private\"}]}",
            "{\"events\":[{\"action\":\"private-card-id\",\"kind\":\"use\",\"count\":1}]}",
            "{\"events\":[{\"action\":\"card_read\",\"kind\":\"private-error\",\"count\":1}]}",
            "{\"events\":[{\"action\":\"card_read\",\"kind\":\"use\",\"count\":1,\"count\":2}]}",
            "{\"events\":[{\"action\":\"card_read\",\"kind\":\"success\",\"count\":2,\"durationMs\":125}]}",
            "{\"events\":[],\"actorId\":\"private\"}",
            "{\"events\":[{\"action\":\"comment_create\",\"kind\":\"use\",\"count\":1,\"content\":\"private-comment\"}]}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/me/activity-client-events") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-StrataAI-Request", "1"); using var rejected = await actor.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Equal(7, observations.Count); Assert.Equal(2, durations.Count);
        }
        using var csrf = await actor.PostAsync("/me/activity-client-events", new StringContent("{}"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using var oversized = new HttpRequestMessage(HttpMethod.Post, "/me/activity-client-events") { Content = new StringContent(new string(' ', 8193)) };
        oversized.Headers.Add("X-StrataAI-Request", "1"); using var tooLarge = await actor.SendAsync(oversized, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode); Assert.Equal(7, observations.Count);
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("Disposable listener outage"));
        using var listenerOutage = await Mutate(actor, HttpMethod.Post, "/me/activity-client-events", payload);
        Assert.Equal(HttpStatusCode.NoContent, listenerOutage.StatusCode);
    }
}
