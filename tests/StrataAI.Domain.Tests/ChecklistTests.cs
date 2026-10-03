using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class ChecklistTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private readonly Guid organization = Guid.NewGuid(), card = Guid.NewGuid(), actor = Guid.NewGuid();
    private Checklist Create() => new(Guid.NewGuid(), organization, card, " Review plan ", RankToken.Initial(), At);
    private ChecklistItem Item(Checklist checklist, string? rank = null) => new(Guid.NewGuid(), organization, checklist.Id, " Inspect roof ", rank ?? RankToken.Initial(), At);

    [Fact]
    public void PRD_13_TC_01_OrderedChildrenCarryStableTenantAndParentIdentity()
    {
        var checklist = Create(); var item = Item(checklist); var next = Item(checklist, RankToken.After(item.Rank));
        Assert.Equal(organization, checklist.OrganizationId); Assert.Equal(card, checklist.CardId);
        Assert.Equal(organization, item.OrganizationId); Assert.Equal(checklist.Id, item.ChecklistId);
        Assert.Equal("Review plan", checklist.Title); Assert.Equal("Inspect roof", item.Text);
        Assert.True(string.CompareOrdinal(item.Rank, next.Rank) < 0);
        Assert.Equal(1, checklist.Version); Assert.Equal(At, item.CreatedAt); Assert.Equal(At, item.UpdatedAt);
    }
    [Fact]
    public void PRD_13_TC_01_CompletionAttributesTheOriginalActorAndUncompletionClearsBothFields()
    {
        var item = Item(Create());
        Assert.True(item.SetCompleted(true, actor, At.AddMinutes(1)));
        Assert.True(item.Completed); Assert.Equal(actor, item.CompletedBy); Assert.Equal(At.AddMinutes(1), item.CompletedAt);
        Assert.False(item.SetCompleted(true, Guid.NewGuid(), At.AddMinutes(2)));
        Assert.Equal(actor, item.CompletedBy); Assert.Equal(2, item.Version); Assert.Equal(At.AddMinutes(1), item.UpdatedAt);
        Assert.True(item.SetCompleted(false, actor, At.AddMinutes(3)));
        Assert.False(item.Completed); Assert.Null(item.CompletedBy); Assert.Null(item.CompletedAt); Assert.Equal(3, item.Version);
    }
    [Fact]
    public void PRD_13_TC_02_EmptyAndDeletedItemsHaveSafeDerivedProgress()
    {
        var checklist = Create(); Assert.Equal(new ChecklistProgress(0, 0), checklist.Progress([]));
        Assert.Equal(0, checklist.Progress([]).Percent);
        var first = Item(checklist); var second = Item(checklist, RankToken.After(first.Rank));
        first.SetCompleted(true, actor, At.AddMinutes(1));
        var progress = checklist.Progress([first, second]);
        Assert.Equal(2, progress.Total); Assert.Equal(1, progress.Completed); Assert.Equal(.5m, progress.Ratio); Assert.Equal(50m, progress.Percent);
        second.Delete(At.AddMinutes(2)); Assert.Equal(100m, checklist.Progress([first, second]).Percent);
        first.Delete(At.AddMinutes(3)); Assert.Equal(0, checklist.Progress([first, second]).Percent);
        Assert.Equal(actor, first.CompletedBy); // Tombstones preserve attribution for retained audit history.
    }
    [Fact]
    public void PRD_13_TC_03_ProgressRejectsForeignAndDuplicateChildren()
    {
        var checklist = Create(); var item = Item(checklist);
        Assert.Throws<ArgumentException>(() => checklist.Progress([Item(Create())]));
        Assert.Throws<ArgumentException>(() => checklist.Progress([new(Guid.NewGuid(), Guid.NewGuid(), checklist.Id, "Other tenant", RankToken.Initial(), At)]));
        Assert.Throws<ArgumentException>(() => checklist.Progress([item, item]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChecklistProgress(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChecklistProgress(-1, 1));
    }
    [Theory]
    [InlineData("")]
    [InlineData("\t\n")]
    [InlineData("invalid\0text")]
    public void PRD_13_TC_03_BlankOrUnsupportedTextNeverMutates(string value)
    {
        var checklist = Create(); var item = Item(checklist);
        Assert.Throws<ArgumentException>(() => checklist.Rename(value, At.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => item.Edit(value, At.AddMinutes(1)));
        Assert.Equal("Review plan", checklist.Title); Assert.Equal("Inspect roof", item.Text);
        Assert.Equal(1, checklist.Version); Assert.Equal(1, item.Version);
    }
    [Fact]
    public void PRD_13_TC_03_InvalidBoundsAndIdentityAreRejected()
    {
        var checklist = Create(); var item = Item(checklist);
        Assert.Throws<ArgumentException>(() => checklist.Rename(new string('x', 161), At));
        Assert.Throws<ArgumentException>(() => item.Edit(new string('x', 2001), At));
        Assert.Throws<ArgumentException>(() => new Checklist(Guid.Empty, organization, card, "Title", RankToken.Initial(), At));
        Assert.Throws<ArgumentException>(() => new Checklist(Guid.NewGuid(), Guid.Empty, card, "Title", RankToken.Initial(), At));
        Assert.Throws<ArgumentException>(() => new Checklist(Guid.NewGuid(), organization, Guid.Empty, "Title", RankToken.Initial(), At));
        Assert.Throws<ArgumentException>(() => new ChecklistItem(Guid.NewGuid(), organization, Guid.Empty, "Text", RankToken.Initial(), At));
        Assert.Throws<ArgumentException>(() => item.SetCompleted(true, Guid.Empty, At));
    }
    [Theory]
    [InlineData("a0")]
    [InlineData("000000000000000000000000000000")]
    [InlineData("999999999999999999999999999999")]
    [InlineData("５00000000000000000000000000000")]
    public void PRD_13_TC_03_RankRejectsNonCanonicalOrExhaustedEndpoints(string rank)
    {
        Assert.False(RankToken.IsValid(rank)); var checklist = Create(); var item = Item(checklist);
        Assert.Throws<ArgumentException>(() => checklist.Reorder(rank, At));
        Assert.Throws<ArgumentException>(() => item.Reorder(rank, At));
    }
    [Fact]
    public void PRD_13_TC_08_FailedBackwardsMutationsPreserveEveryFieldAndRevision()
    {
        var checklist = Create(); var item = Item(checklist); var earlier = At.AddSeconds(-1);
        Assert.Throws<ArgumentOutOfRangeException>(() => checklist.Rename("Changed", earlier));
        Assert.Throws<ArgumentOutOfRangeException>(() => checklist.Reorder(RankToken.After(checklist.Rank), earlier));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.Edit("Changed", earlier));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.Reorder(RankToken.After(item.Rank), earlier));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.SetCompleted(true, actor, earlier));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.Delete(earlier));
        Assert.Throws<ArgumentOutOfRangeException>(() => checklist.Delete(earlier));
        Assert.Equal("Review plan", checklist.Title); Assert.Equal("Inspect roof", item.Text);
        Assert.Equal(RankToken.Initial(), checklist.Rank); Assert.Equal(RankToken.Initial(), item.Rank);
        Assert.Null(item.CompletedAt); Assert.Null(item.CompletedBy); Assert.False(item.Completed); Assert.Null(item.DeletedAt);
        Assert.Equal(1, checklist.Version); Assert.Equal(1, item.Version); Assert.Equal(At, item.UpdatedAt);
    }
    [Fact]
    public void PRD_13_TC_10_TombstonesRetainContentAndRejectLaterEdits()
    {
        var checklist = Create(); var item = Item(checklist);
        Assert.True(checklist.Delete(At.AddMinutes(1))); Assert.False(checklist.Delete(At.AddMinutes(2)));
        Assert.True(item.Delete(At.AddMinutes(1))); Assert.False(item.Delete(At.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => checklist.Rename("Changed", At.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => checklist.Reorder(RankToken.After(checklist.Rank), At.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => item.Edit("Changed", At.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => item.SetCompleted(true, actor, At.AddMinutes(2)));
        Assert.Equal("Review plan", checklist.Title); Assert.Equal("Inspect roof", item.Text); Assert.Equal(2, item.Version);
    }
    [Fact]
    public void PRD_13_TC_07_NoOpsDoNotAdvanceRevisionAndChangedWritesNormalizeUtc()
    {
        var checklist = Create(); var item = Item(checklist);
        Assert.False(checklist.Rename(" Review plan ", At)); Assert.False(checklist.Reorder(checklist.Rank, At));
        Assert.False(item.Edit(" Inspect roof ", At)); Assert.False(item.Reorder(item.Rank, At)); Assert.False(item.SetCompleted(false, actor, At));
        Assert.Equal(1, item.Version); Assert.Equal(1, checklist.Version);
        Assert.True(item.Edit("Edited", At.AddHours(1).ToOffset(TimeSpan.FromHours(-7))));
        Assert.Equal(TimeSpan.Zero, item.UpdatedAt.Offset); Assert.Equal(At.AddHours(1), item.UpdatedAt); Assert.Equal(2, item.Version);
    }
}
