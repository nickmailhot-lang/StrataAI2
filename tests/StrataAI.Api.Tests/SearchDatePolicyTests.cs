using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Search_exposes_current_authorized_Board_date_policy_and_clearing_without_private_disclosure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(outsider);
        var boardId = await TelemetryBoard(owner, ct);
        using var listReply = await Mutate(owner, HttpMethod.Post, $"/boards/{boardId}/lists", new { name = "Date policy List" });
        var listId = (await listReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardReply = await Mutate(owner, HttpMethod.Post, $"/lists/{listId}/cards", new { title = "Private date policy needle" });
        Assert.Equal(HttpStatusCode.Created, cardReply.StatusCode);
        var cardId = (await cardReply.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        bool expectedTimed = false;
        DateTimeOffset? expectedDue = null;
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var policies = app.Services.GetRequiredService<BoardDatePolicyService>();
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        async Task AssertPolicy(string? timezone)
        {
            using var response = await owner.GetAsync("/search?q=date%20policy%20needle", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.Private); Assert.True(response.Headers.CacheControl.NoStore);
            var page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var row = Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal(timezone, row.GetProperty("boardDateTimezone").GetString());
            var card = row.GetProperty("card");
            Assert.Equal(expectedTimed, card.GetProperty("dueHasTime").GetBoolean());
            Assert.Equal(expectedDue, card.GetProperty("dueAt").ValueKind == JsonValueKind.Null
                ? null : card.GetProperty("dueAt").GetDateTimeOffset());
        }
        await AssertPolicy(null);
        using var timed = await Mutate(owner, HttpMethod.Patch, $"/cards/{cardId}/dates", new {
            startAt = (string?)null, dueAt = "2040-01-02T00:30:00Z", dueTimezone = "UTC",
            dueHasTime = true, dueComplete = false, version = 1,
        });
        Assert.Equal(HttpStatusCode.OK, timed.StatusCode);
        expectedTimed = true; expectedDue = DateTimeOffset.Parse("2040-01-02T00:30:00Z");
        await AssertPolicy(null);
        foreach (var timezone in new string?[] { "Pacific/Honolulu", "Asia/Tokyo", null })
        {
            var board = (await store.FindBoardAsync(boardId, ct))!;
            Assert.True((await policies.SetAsync(boardId, actor, new(timezone, board.Version), "fixture", ct)).Succeeded);
            await AssertPolicy(timezone);
            using var denied = await outsider.GetAsync("/search?q=date%20policy%20needle", ct);
            Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
            var page = await denied.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Empty(page.GetProperty("items").EnumerateArray());
            var body = page.GetRawText();
            Assert.DoesNotContain("Private date policy needle", body);
            Assert.DoesNotContain("Pacific/Honolulu", body); Assert.DoesNotContain("Asia/Tokyo", body);
        }
        using var dateOnly = await Mutate(owner, HttpMethod.Patch, $"/cards/{cardId}/dates", new {
            startAt = (string?)null, dueAt = "2040-01-02", dueTimezone = "Pacific/Honolulu",
            dueHasTime = false, dueComplete = false, version = 2,
        });
        Assert.Equal(HttpStatusCode.OK, dateOnly.StatusCode);
        expectedTimed = false; expectedDue = DateTimeOffset.Parse("2040-01-03T09:59:59.999999Z");
        await AssertPolicy(null);
        var current = (await store.FindBoardAsync(boardId, ct))!;
        Assert.True((await policies.SetAsync(boardId, actor, new("Pacific/Honolulu", current.Version), "fixture", ct)).Succeeded);
        await AssertPolicy("Pacific/Honolulu");
    }
}
