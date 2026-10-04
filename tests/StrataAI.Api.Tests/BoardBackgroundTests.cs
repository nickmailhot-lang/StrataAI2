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
    public async Task PRD_04_Built_in_backgrounds_are_canonical_and_unknown_selections_leave_state_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        foreach (var color in BoardBackgroundPolicy.Colors)
        {
            using var created = await owner.PostAsJsonAsync("/boards", new { organizationId = f.Organization, name = "Palette " + color,
                visibility = "PRIVATE", backgroundType = " color ", backgroundValue = " " + color.ToUpperInvariant() + " " }, ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var record = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("COLOR", record.GetProperty("backgroundType").GetString());
            Assert.Equal(color, record.GetProperty("backgroundValue").GetString());
        }
        using var boardResponse = await owner.PostAsJsonAsync("/boards", new { organizationId = f.Organization, name = "Reviewed palette", visibility = "PRIVATE" }, ct);
        Assert.Equal(HttpStatusCode.Created, boardResponse.StatusCode);
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>(ct); var id = board.GetProperty("id").GetGuid();
        foreach (var value in new[] { "unknown", "#ffffff", "url(https://example.test/private)", "https://example.test/image.png" })
        {
            using var denied = await owner.PatchAsJsonAsync($"/boards/{id}", new { name = "Should not change", version = 1, backgroundType = "COLOR", backgroundValue = value }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.Contains("invalid_background", await denied.Content.ReadAsStringAsync(ct));
            var current = await owner.GetFromJsonAsync<JsonElement>($"/boards/{id}", ct);
            Assert.Equal("Reviewed palette", current.GetProperty("board").GetProperty("name").GetString());
            Assert.Equal(1, current.GetProperty("board").GetProperty("version").GetInt64());
            using var unauthorized = await member.PatchAsJsonAsync($"/boards/{id}", new { name = "Denied", version = 1, backgroundType = "COLOR", backgroundValue = value }, ct);
            Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);
            Assert.DoesNotContain("invalid_background", await unauthorized.Content.ReadAsStringAsync(ct));
        }
        using var selected = await owner.PatchAsJsonAsync($"/boards/{id}", new { name = "Reviewed palette", version = 1, backgroundType = "COLOR", backgroundValue = " PURPLE " }, ct);
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var result = await selected.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("purple", result.GetProperty("backgroundValue").GetString()); Assert.Equal(2, result.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task PRD_04_Metadata_only_edits_preserve_historical_backgrounds_without_selecting_them_again()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var id = Guid.NewGuid();
        await store.CreateBoardAsync(f.Organization, f.Owner, id, "Historical Board", null, BoardVisibility.Private,
            "COLOR", "legacy-custom-color", DateTimeOffset.UtcNow, ct);
        using var renamed = await owner.PatchAsJsonAsync($"/boards/{id}", new { name = "Renamed Board", description = "Updated description", version = 1 }, ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var record = await renamed.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("legacy-custom-color", record.GetProperty("backgroundValue").GetString());
        Assert.Equal("Updated description", record.GetProperty("description").GetString());
        Assert.Equal(2, record.GetProperty("version").GetInt64());
    }
}
