using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Onboarding;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-03-TC-05 / PRD-60: a cookie switch must not reuse another actor's consent.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invitation_revocation_rejects_a_different_reviewed_actor_before_changing_the_invitation(bool boardSurface)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var creation = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Reviewed invitation scope" });
        var org = (await creation.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        BoardInvitationTarget? target = null;
        if (boardSurface)
        {
            var created = await app.Services.GetRequiredService<IWorkManagementService>().CreateBoardAsync(org, actor,
                "Reviewed invitation Board", null, BoardVisibility.Private, "COLOR", "blue", "fixture", ct);
            Assert.True(created.Succeeded, created.ErrorCode);
            target = new(created.Value!.Id, BoardRole.Member);
        }
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        await app.Services.GetRequiredService<IInvitationStore>().CreateAsync(new(id, org, "reviewed@example.test",
            "REVIEWED@EXAMPLE.TEST", new string('a', 64), InvitationSurface.Internal, "MEMBER", actor, now,
            now.AddDays(7), null, null, BoardTarget: target), ct);
        var root = target is null ? $"/organizations/{org}/invitations" : $"/boards/{target.BoardId}/invitations";
        foreach (var reviewed in new[] { Guid.NewGuid(), Guid.Empty })
        {
            using var refused = await Mutate(owner, HttpMethod.Delete, $"{root}/{id}?expectedActorId={reviewed}", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            var body = await refused.Content.ReadAsStringAsync(ct);
            Assert.Contains("session_unavailable", body, StringComparison.Ordinal);
            Assert.DoesNotContain("reviewed@example.test", body, StringComparison.Ordinal);
            var history = await owner.GetFromJsonAsync<JsonElement>(root, ct);
            Assert.Equal(JsonValueKind.Null, Assert.Single(history.GetProperty("items").EnumerateArray()).GetProperty("revokedAt").ValueKind);
        }
        using var accepted = await Mutate(owner, HttpMethod.Delete, $"{root}/{id}?expectedActorId={actor}", new { });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        var after = await owner.GetFromJsonAsync<JsonElement>(root, ct);
        Assert.Equal(JsonValueKind.String, Assert.Single(after.GetProperty("items").EnumerateArray()).GetProperty("revokedAt").ValueKind);
    }
}
