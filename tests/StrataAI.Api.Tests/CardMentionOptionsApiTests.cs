using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_15_HTTPMentionOptionsUseActualCurrentCookieAndOnlyCurrentBoardParticipantMetadata()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Mention context", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/mention-options";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
        using var response = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var page = (await response.Content.ReadFromJsonAsync<CardMentionOptionsPage>(ct))!;
        Assert.Equal(card.Id, page.CardId); Assert.Equal(f.Organization, page.OrganizationId); Assert.Equal(f.Board, page.BoardId);
        Assert.Equal(new[] { f.Owner, f.Recipient }.OrderBy(x => $"u_{x:N}", StringComparer.Ordinal), page.Items.Select(x => x.UserId));
        Assert.All(page.Items, row => { Assert.Equal($"u_{row.UserId:N}", row.Handle); Assert.Equal(1, row.HandleVersion); });
        var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.All(json.RootElement.GetProperty("items").EnumerateArray(), row =>
            Assert.Equal(new[] { "displayName", "handle", "handleVersion", "userId" }, row.EnumerateObject().Select(p => p.Name).Order()));
        foreach (var suffix in new[] { "?prefix=%40member", "?prefix=member&prefix=other", "?after=x&after=y", "?after=", "?after=" + new string('x', 161) })
        {
            using var denied = await owner.GetAsync(path + suffix, ct);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode); Assert.True(denied.Headers.CacheControl!.NoStore);
            Assert.DoesNotContain($"u_{f.Owner:N}", await denied.Content.ReadAsStringAsync(ct));
        }
        var target = await member.GetFromJsonAsync<CardMentionOptionsPage>(path + $"?prefix=u_{f.Owner:N}", ct);
        Assert.Equal(f.Owner, Assert.Single(target!.Items).UserId);
        await work.RemoveBoardMemberAsync(f.Board, f.Recipient, DateTimeOffset.UtcNow, ct);
        using var revoked = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.DoesNotContain($"u_{f.Owner:N}", await revoked.Content.ReadAsStringAsync(ct));
        Assert.Equal(f.Owner, Assert.Single((await owner.GetFromJsonAsync<CardMentionOptionsPage>(path, ct))!.Items).UserId);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(path, ct)).StatusCode);
    }
}
