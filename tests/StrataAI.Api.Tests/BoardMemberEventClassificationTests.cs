using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-05 public events are distinct from the private authority-proof source.
    [Theory]
    [InlineData(null, BoardRole.Member, false, "BOARD_MEMBER_ADDED")]
    [InlineData(null, BoardRole.Admin, false, "BOARD_MEMBER_ADDED")]
    [InlineData(BoardRole.Member, BoardRole.Admin, false, "BOARD_MEMBER_ROLE_CHANGED")]
    [InlineData(BoardRole.Admin, BoardRole.Member, false, "BOARD_MEMBER_ROLE_CHANGED")]
    [InlineData(BoardRole.Admin, BoardRole.Member, true, "BOARD_MEMBER_ADDED")]
    [InlineData(BoardRole.Member, BoardRole.Member, false, "BOARD_MEMBER_UPDATED")]
    public async Task PRD_05_member_events_classify_actual_grant_transition(BoardRole? prior, BoardRole next, bool removed, string expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); var f = await BoardInvitationFixtureAsync(app, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        if (prior is { } role) Assert.True((await work.SetBoardMemberAsync(f.Board.Id, f.Owner.Id, f.Recipient.Id, role, "prepare", ct)).Succeeded);
        if (removed) Assert.True((await work.RemoveBoardMemberAsync(f.Board.Id, f.Owner.Id, f.Recipient.Id, "remove", ct)).Succeeded);
        var before = await store.FindBoardMemberAsync(f.Board.Id, f.Recipient.Id, ct);
        var correlation = Guid.NewGuid().ToString("N");
        var changed = await work.SetBoardMemberAsync(f.Board.Id, f.Owner.Id, f.Recipient.Id, next, correlation, ct,
            before is { Active: true } ? before.Version : null);
        Assert.True(changed.Succeeded, changed.ErrorCode);
        Assert.Equal(next, changed.Value!.Role); Assert.True(changed.Value.Active);
        Assert.Equal((before?.Version ?? 0) + 1, changed.Value.Version);
        var journal = await app.Services.GetRequiredService<IWorkEventReader>().ReadAsync(f.Board.OrganizationId, f.Board.Id, 0, 100, ct);
        var source = Assert.Single(journal.Events, row => row.Event.CorrelationId == correlation).Event;
        Assert.Equal(expected, source.EventType); Assert.NotEqual(Guid.Empty, source.EventId);
        Assert.Equal(f.Owner.Id, source.ActorId); Assert.Equal(f.Board.OrganizationId, source.OrganizationId);
        Assert.Equal(f.Board.Id, source.BoardId); Assert.Equal("Board", source.EntityType);
        Assert.Equal(f.Board.Id, source.EntityId); Assert.Equal(f.Board.Version, source.Version);
        Assert.True(source.CreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task PRD_05_member_events_HTTP_receipt_replay_does_not_duplicate_transition()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var reader = app.Services.GetRequiredService<IWorkEventReader>();
        var before = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        var ids = before.Events.Select(row => row.Event.EventId).ToHashSet(); var key = Guid.NewGuid().ToString();
        using var changed = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" }, key);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode); var receipt = await changed.Content.ReadAsStringAsync(ct);
        using var recovered = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" }, key);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode); Assert.Equal(receipt, await recovered.Content.ReadAsStringAsync(ct));
        var after = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        var source = Assert.Single(after.Events, row => !ids.Contains(row.Event.EventId)).Event;
        Assert.Equal("BOARD_MEMBER_ROLE_CHANGED", source.EventType); Assert.Equal(f.Owner, source.ActorId);
        var member = await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardMemberAsync(f.Board, f.Recipient, ct);
        using var receiptJson = JsonDocument.Parse(receipt);
        Assert.Equal(receiptJson.RootElement.GetProperty("version").GetInt64(), member!.Version);
    }

    [Fact]
    public async Task PRD_05_member_events_refused_changes_leave_membership_and_journal_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); var f = await BoardInvitationFixtureAsync(app, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>(); var store = app.Services.GetRequiredService<IWorkManagementStore>();
        Assert.True((await work.SetBoardMemberAsync(f.Board.Id, f.Owner.Id, f.Recipient.Id, BoardRole.Member, "prepare", ct)).Succeeded);
        var before = await store.FindBoardMemberAsync(f.Board.Id, f.Recipient.Id, ct);
        var reader = app.Services.GetRequiredService<IWorkEventReader>(); var journal = await reader.ReadAsync(f.Board.OrganizationId, f.Board.Id, 0, 100, ct);
        var stale = await work.SetBoardMemberAsync(f.Board.Id, f.Owner.Id, f.Recipient.Id, BoardRole.Admin, "stale", ct, before!.Version + 1);
        Assert.Equal("version_conflict", stale.ErrorCode);
        var denied = await work.SetBoardMemberAsync(f.Board.Id, f.Recipient.Id, f.Recipient.Id, BoardRole.Admin, "denied", ct, before.Version);
        Assert.Equal("board_not_found", denied.ErrorCode);
        Assert.Equal(before, await store.FindBoardMemberAsync(f.Board.Id, f.Recipient.Id, ct));
        Assert.Equal(journal.Events.Select(row => row.Event), (await reader.ReadAsync(f.Board.OrganizationId, f.Board.Id, 0, 100, ct)).Events.Select(row => row.Event));
    }

    [Fact]
    public async Task PRD_05_member_events_late_admission_failure_rolls_back_source_and_receipt_before_same_key_retry()
    {
        var ct = TestContext.Current.CancellationToken; var fence = new MemberEventActorFence();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(fence));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var reader = app.Services.GetRequiredService<IWorkEventReader>();
        var before = await store.FindBoardMemberAsync(f.Board, f.Recipient, ct);
        var journal = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        fence.Admission = async () => (await store.FindBoardMemberAsync(f.Board, f.Recipient, ct))!.Role != BoardRole.Admin;
        var key = Guid.NewGuid().ToString();
        using var refused = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" }, key);
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Equal(before, await store.FindBoardMemberAsync(f.Board, f.Recipient, ct));
        Assert.Equal(journal.Events.Select(row => row.Event), (await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct)).Events.Select(row => row.Event));
        fence.Admission = null;
        using var committed = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" }, key);
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        var after = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        var added = Assert.Single(after.Events, row => !journal.Events.Any(old => old.Event.EventId == row.Event.EventId));
        Assert.Equal("BOARD_MEMBER_ROLE_CHANGED", added.Event.EventType);
        using var recovered = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" }, key);
        Assert.Equal(await committed.Content.ReadAsStringAsync(ct), await recovered.Content.ReadAsStringAsync(ct));
        Assert.Equal(after.Events.Select(row => row.Event), (await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct)).Events.Select(row => row.Event));
    }

    private sealed class MemberEventActorFence : ICommandActorAuthorization
    {
        public Func<Task<bool>>? Admission { get; set; }
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Admission?.Invoke() ?? Task.FromResult(true); }
    }
}
