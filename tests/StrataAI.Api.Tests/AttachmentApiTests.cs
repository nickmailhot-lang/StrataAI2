using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_14_URL_creation_has_one_canonical_receipt_and_preserves_other_card_fields_on_replay_conflict_and_revocation()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "URL card", "Preserved description", null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/attachments"; var input = new CreateUrlAttachmentInput(" Link ", "https://example.test/path?q=1#section", 1);
        var key = Guid.NewGuid().ToString();
        using var created = await Mutate(member, HttpMethod.Post, path + "/url", input, key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); var receipt = await created.Content.ReadAsStringAsync(ct);
        var change = (await created.Content.ReadFromJsonAsync<AttachmentChange>(ct))!;
        Assert.Equal(2, change.CardVersion); Assert.Equal(f.Recipient, change.Attachment.UploaderId);
        Assert.Equal("Link", change.Attachment.DisplayName); Assert.Equal(AttachmentKind.Url, change.Attachment.Kind);
        Assert.Equal(AttachmentScanStatus.NotApplicable, change.Attachment.ScanStatus); Assert.Null(change.Attachment.MimeType); Assert.Null(change.Attachment.SizeBytes);
        Assert.DoesNotContain("storageKey", receipt, StringComparison.OrdinalIgnoreCase);
        using var replay = await Mutate(member, HttpMethod.Post, path + "/url", input, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using var reused = await Mutate(member, HttpMethod.Post, path + "/url", input with { Title = "Different" }, key);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Post, path + "/url", input);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var page = (await member.GetFromJsonAsync<AttachmentPage>(path, ct))!;
        Assert.Equal(change.Attachment, Assert.Single(page.Items)); Assert.True(page.CanEdit); Assert.Equal(2, page.CardVersion);
        var current = (await work.FindCardAsync(card.Id, ct))!;
        Assert.Equal(card.Title, current.Title); Assert.Equal(card.Description, current.Description); Assert.Equal(card.Rank, current.Rank); Assert.Equal(2, current.Version);
        using var invalidOutsider = await Mutate(outsider, HttpMethod.Post, path + "/url", new CreateUrlAttachmentInput("", "javascript:bad", 0));
        Assert.Equal(HttpStatusCode.NotFound, invalidOutsider.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path + "?after=malformed", ct)).StatusCode);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(path, ct)).StatusCode);
        using var revokedReplay = await Mutate(member, HttpMethod.Post, path + "/url", input, key);
        Assert.Equal(HttpStatusCode.NotFound, revokedReplay.StatusCode);
        Assert.Equal(change.Attachment, Assert.Single((await owner.GetFromJsonAsync<AttachmentPage>(path, ct))!.Items));
    }
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>bad</script>")]
    [InlineData("file:///private")]
    [InlineData("https://user:password@example.test/")]
    public async Task PRD_14_Unsafe_URLs_never_change_card_or_create_metadata(string url)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Unchanged", null, null, DateTimeOffset.UtcNow, ct); var path = $"/cards/{card.Id}/attachments";
        using var invalid = await Mutate(owner, HttpMethod.Post, path + "/url", new CreateUrlAttachmentInput("Unsafe", url, 1));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(card, await work.FindCardAsync(card.Id, ct)); Assert.Empty((await owner.GetFromJsonAsync<AttachmentPage>(path, ct))!.Items);
    }
    [Fact]
    public async Task PRD_14_Pages_seek_50_plus_13_require_card_bound_cursors_and_exclude_public_nonmembers()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var metadata = app.Services.GetRequiredService<IAttachmentMetadataStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Paged", null, null, DateTimeOffset.UtcNow, ct); var path = $"/cards/{card.Id}/attachments";
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 63; i++) await metadata.CreateUrlAttachmentAsync(Guid.NewGuid(), f.Organization, card.Id, f.Owner, $"Link {i}", $"https://example.test/{i}", now, ct);
        var first = (await member.GetFromJsonAsync<AttachmentPage>(path, ct))!; Assert.Equal(50, first.Items.Count); Assert.NotNull(first.NextCursor);
        var second = (await member.GetFromJsonAsync<AttachmentPage>(path + "?after=" + Uri.EscapeDataString(first.NextCursor), ct))!;
        Assert.Equal(13, second.Items.Count); Assert.Null(second.NextCursor);
        Assert.Equal(63, first.Items.Concat(second.Items).Select(row => row.Id).Distinct().Count());
        var badCursor = first.NextCursor.Replace(card.Id.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.GetAsync(path + "?after=" + Uri.EscapeDataString(badCursor), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.GetAsync(path + "?after=bad", ct)).StatusCode);
        var board = (await work.FindBoardAsync(f.Board, ct))!;
        Assert.NotNull(await work.SetBoardVisibilityAsync(f.Board, BoardVisibility.Public, board.Version, now, ct));
        Assert.Equal(HttpStatusCode.OK, (await outsider.GetAsync($"/boards/{f.Board}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path + "?after=bad", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
        await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(path + "?after=bad", ct)).StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_14_Archived_card_or_list_retains_readable_metadata_and_refuses_creation_and_previous_receipt(bool listParent)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Retained", null, null, DateTimeOffset.UtcNow, ct); var path = $"/cards/{card.Id}/attachments";
        var key = Guid.NewGuid().ToString(); var input = new CreateUrlAttachmentInput("Retained", "https://example.test/", 1);
        using var added = await Mutate(owner, HttpMethod.Post, path + "/url", input, key); Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var attachment = (await added.Content.ReadFromJsonAsync<AttachmentChange>(ct))!.Attachment;
        var parentPath = listParent ? $"/lists/{f.List}" : $"/cards/{card.Id}";
        using var archived = await Mutate(owner, HttpMethod.Post, parentPath + "/archive", new { version = listParent ? 1 : 2 }); Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        var page = (await member.GetFromJsonAsync<AttachmentPage>(path, ct))!; Assert.False(page.CanEdit); Assert.Equal(attachment, Assert.Single(page.Items));
        using var archivedReplay = await Mutate(owner, HttpMethod.Post, path + "/url", input, key); Assert.Equal(HttpStatusCode.NotFound, archivedReplay.StatusCode);
        using var newDenied = await Mutate(owner, HttpMethod.Post, path + "/url", input with { CardVersion = listParent ? 2 : 3 }); Assert.Equal(HttpStatusCode.NotFound, newDenied.StatusCode);
        using var deleted = await Mutate(owner, HttpMethod.Delete, parentPath + (listParent ? "?version=2&confirmed=true&containedCardCount=1" : "?version=3&confirmed=true"), new { });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(path, ct)).StatusCode);
        Assert.Equal(attachment, await app.Services.GetRequiredService<IAttachmentMetadataStore>().FindAttachmentAsync(f.Organization, card.Id, attachment.Id, ct));
    }
}
