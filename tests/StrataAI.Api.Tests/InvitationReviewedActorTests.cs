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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invitation_creation_rejects_a_different_reviewed_actor_without_consuming_the_retry_key(bool boardSurface)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var creation = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Reviewed creation scope" });
        var org = (await creation.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var root = $"/organizations/{org}/invitations";
        object input = new { email = "reviewed-create@example.test", surface = "INTERNAL", targetRole = "MEMBER" };
        if (boardSurface)
        {
            var board = await app.Services.GetRequiredService<IWorkManagementService>().CreateBoardAsync(org, actor,
                "Reviewed creation Board", null, BoardVisibility.Private, "COLOR", "blue", "fixture", ct);
            Assert.True(board.Succeeded, board.ErrorCode);
            root = $"/boards/{board.Value!.Id}/invitations";
            input = new { email = "reviewed-create@example.test", role = "MEMBER" };
        }
        var retryKey = Guid.NewGuid().ToString("D");
        foreach (var reviewed in new[] { Guid.NewGuid(), Guid.Empty })
        {
            using var refused = await Mutate(owner, HttpMethod.Post, $"{root}?expectedActorId={reviewed}", input, retryKey);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            var body = await refused.Content.ReadAsStringAsync(ct);
            Assert.Contains("session_unavailable", body, StringComparison.Ordinal);
            Assert.DoesNotContain("reviewed-create@example.test", body, StringComparison.Ordinal);
            var history = await owner.GetFromJsonAsync<JsonElement>(root, ct);
            Assert.Empty(history.GetProperty("items").EnumerateArray());
        }
        using var accepted = await Mutate(owner, HttpMethod.Post, $"{root}?expectedActorId={actor}", input, retryKey);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var original = await accepted.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var retry = await Mutate(owner, HttpMethod.Post, $"{root}?expectedActorId={actor}", input, retryKey);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(original.GetProperty("id").GetGuid(), (await retry.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid());
        var final = await owner.GetFromJsonAsync<JsonElement>(root, ct);
        Assert.Equal(original.GetProperty("id").GetGuid(), Assert.Single(final.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    // PRD-60-TC-04/06/07: bind recipient disclosure and acceptance to the
    // reviewed account before cursor parsing, protected reads or a mutation.
    [Theory]
    [InlineData("INTERNAL")]
    [InlineData("PORTAL")]
    [InlineData("BOARD")]
    public async Task Recipient_invitation_rejects_a_different_reviewed_actor_without_disclosure_or_acceptance(string surface)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(recipient);
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var user = await recipient.GetFromJsonAsync<JsonElement>("/me", ct);
        var actor = user.GetProperty("id").GetGuid(); var email = user.GetProperty("email").GetString();
        using var creation = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Protected recipient scope" });
        var org = (await creation.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var root = $"/organizations/{org}/invitations"; Guid? boardId = null;
        object input = new { email, surface = surface == "PORTAL" ? "PORTAL" : "INTERNAL", targetRole = surface == "PORTAL" ? "OWNER" : "MEMBER" };
        if (surface == "BOARD")
        {
            using var enrolled = await Mutate(owner, HttpMethod.Post, root, new { email, surface = "INTERNAL", targetRole = "MEMBER" });
            var enrollmentId = (await enrolled.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            using var enrollment = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{enrollmentId}/accept", new { });
            Assert.Equal(HttpStatusCode.OK, enrollment.StatusCode);
            var board = await app.Services.GetRequiredService<IWorkManagementService>().CreateBoardAsync(org, ownerId,
                "Protected recipient Board", null, BoardVisibility.Private, "COLOR", "blue", "fixture", ct);
            Assert.True(board.Succeeded, board.ErrorCode); boardId = board.Value!.Id;
            root = $"/boards/{boardId}/invitations"; input = new { email, role = "MEMBER" };
        }
        using var issued = await Mutate(owner, HttpMethod.Post, root, input);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var issuedInvitation = await issued.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = issuedInvitation.GetProperty("id").GetGuid();
        var token = issuedInvitation.GetProperty("invitationToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        foreach (var expected in new[] { Guid.NewGuid(), Guid.Empty })
        {
            using var read = await recipient.GetAsync($"/me/invitations?expectedActorId={expected}&after=invalid", ct);
            Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
            using var write = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{id}/accept?expectedActorId={expected}", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
            using var review = await Mutate(recipient, HttpMethod.Post, $"/invitations/review?expectedActorId={expected}", new { token });
            Assert.Equal(HttpStatusCode.Unauthorized, review.StatusCode);
            foreach (var response in new[] { read, write, review })
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                Assert.Contains("session_unavailable", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Protected recipient", text, StringComparison.Ordinal);
                Assert.DoesNotContain(id.ToString(), text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(token!, text, StringComparison.Ordinal);
            }
            var history = await owner.GetFromJsonAsync<JsonElement>(root, ct);
            var original = history.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == id);
            Assert.Equal(JsonValueKind.Null, original.GetProperty("acceptedAt").ValueKind);
            if (boardId is { } board) Assert.Null(await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardMemberAsync(board, actor, ct));
        }
        using var confirmedReview = await Mutate(recipient, HttpMethod.Post, $"/invitations/review?expectedActorId={actor}", new { token });
        Assert.Equal(HttpStatusCode.OK, confirmedReview.StatusCode);
        Assert.Equal(id, (await confirmedReview.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid());
        var page = await recipient.GetFromJsonAsync<JsonElement>($"/me/invitations?expectedActorId={actor}", ct);
        Assert.Equal(id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using var accepted = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{id}/accept?expectedActorId={actor}", new { });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode); var originalAck = await accepted.Content.ReadAsStringAsync(ct);
        using var retry = await Mutate(recipient, HttpMethod.Post, $"/me/invitations/{id}/accept?expectedActorId={actor}", new { });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(originalAck, await retry.Content.ReadAsStringAsync(ct));
        Assert.Empty((await recipient.GetFromJsonAsync<JsonElement>($"/me/invitations?expectedActorId={actor}", ct)).GetProperty("items").EnumerateArray());
    }

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
