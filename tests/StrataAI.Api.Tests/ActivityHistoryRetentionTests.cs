using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Activity_history_survives_session_and_cursor_expiry_with_fresh_current_admission()
    {
        var ct = TestContext.Current.CancellationToken;
        // Start in the client's real present, then advance the server clock.
        // Issuing absolute-expiry cookies five years in the past would cause
        // the HTTP client to drop them before the initial fixture can sign in.
        var current = DateTimeOffset.UtcNow.AddYears(5); var clock = new ReceiptTestClock { UtcNow = current.AddYears(-5) };
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var email = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("email").GetString();
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "Retained history" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        for (var version = 1; version <= 65; version++)
        {
            using var updated = await Mutate(owner, HttpMethod.Patch, $"/cards/{card}", new { title = $"Retained history {version}", version });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }
        var path = $"/cards/{card}/activity";
        var oldPage = await owner.GetFromJsonAsync<JsonElement>(path, ct);
        var oldCursor = oldPage.GetProperty("nextCursor").GetString(); Assert.NotNull(oldCursor);
        Assert.Equal(50, oldPage.GetProperty("items").GetArrayLength());
        clock.UtcNow = current;
        using var expiredSession = await owner.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, expiredSession.StatusCode);
        using var freshLogin = await Mutate(owner, HttpMethod.Post, "/auth/login", new { email, password = "api-host-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, freshLogin.StatusCode);
        using var expiredCursor = await owner.GetAsync($"{path}?after={Uri.EscapeDataString(oldCursor)}", ct);
        Assert.Equal(HttpStatusCode.BadRequest, expiredCursor.StatusCode);
        using var currentChange = await Mutate(owner, HttpMethod.Patch, $"/cards/{card}", new { title = "Current change", version = 66 });
        Assert.Equal(HttpStatusCode.OK, currentChange.StatusCode);
        var first = await owner.GetFromJsonAsync<JsonElement>(path, ct);
        var rows = first.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(50, rows.Length);
        Assert.Equal(current.Year, rows[0].GetProperty("createdAt").GetDateTimeOffset().Year);
        Assert.All(rows.Skip(1), row => Assert.Equal(current.AddYears(-5).Year, row.GetProperty("createdAt").GetDateTimeOffset().Year));
        var cursor = first.GetProperty("nextCursor").GetString(); Assert.NotNull(cursor);
        var tail = await owner.GetFromJsonAsync<JsonElement>($"{path}?after={Uri.EscapeDataString(cursor)}", ct);
        var rest = tail.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(17, rest.Length);
        Assert.Equal(JsonValueKind.Null, tail.GetProperty("nextCursor").ValueKind);
        Assert.Equal(67, rows.Concat(rest).Select(row => row.GetProperty("eventId").GetGuid()).Distinct().Count());
        Assert.All(rest, row => { Assert.Equal(current.AddYears(-5).Year, row.GetProperty("createdAt").GetDateTimeOffset().Year);
            Assert.Empty(row.GetProperty("metadata").EnumerateObject()); });
    }
}
