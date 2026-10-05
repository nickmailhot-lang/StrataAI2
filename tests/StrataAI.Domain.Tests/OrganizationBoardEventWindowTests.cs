using StrataAI.Application.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationBoardEventWindowTests
{
    private static OrganizationBoardEventCandidate Row(long sequence, bool ready = true) =>
        new(sequence, Guid.NewGuid(), Guid.NewGuid(), "BOARD_ARCHIVED", 2, DateTimeOffset.UtcNow, ready);

    [Fact]
    public void OtherBoardsGapsDoNotCauseResetOrExposeTheirEnvelopes()
    {
        var rows = new[] { Row(3), Row(8) };
        var page = OrganizationBoardEventWindow.Build(0, 10, 2, rows);
        Assert.Equal(10, page.Cursor);
        Assert.False(page.ResetRequired);
        Assert.Equal(rows, page.Events);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void PendingEligibleSourceCannotBeSkippedByLaterReadySource()
    {
        var rows = new[] { Row(3), Row(8, false), Row(9) };
        var page = OrganizationBoardEventWindow.Build(0, 10, 2, rows);
        Assert.Equal(3, page.Cursor);
        Assert.True(page.Pending);
        Assert.Single(page.Events);
        rows[1] = rows[1] with { Ready = true };
        var recovered = OrganizationBoardEventWindow.Build(3, 10, 2, rows[1..]);
        Assert.Equal(10, recovered.Cursor);
        Assert.Equal(rows[1..], recovered.Events);
    }

    [Fact]
    public void FullPageDoesNotConsumeTheLookaheadEvent()
    {
        var rows = new[] { Row(3), Row(8), Row(10) };
        var page = OrganizationBoardEventWindow.Build(0, 10, 2, rows);
        Assert.Equal(8, page.Cursor);
        Assert.True(page.HasMore);
        Assert.Equal(rows[..2], page.Events);
        var next = OrganizationBoardEventWindow.Build(page.Cursor, 10, 2, rows[2..]);
        Assert.Single(next.Events);
        Assert.Equal(rows[2].EventId, next.Events[0].EventId);
    }

    [Fact]
    public void MalformedOrderingAndFutureCursorDiscardEveryEnvelope()
    {
        foreach (var page in new[] {
            OrganizationBoardEventWindow.Build(11, 10, 2, []),
            OrganizationBoardEventWindow.Build(0, 10, 2, [Row(3), Row(3)]),
            OrganizationBoardEventWindow.Build(0, 10, 2, [Row(3), Row(11)]) })
        {
            Assert.True(page.ResetRequired);
            Assert.Empty(page.Events);
            Assert.Equal(0, page.Cursor);
        }
    }
}
