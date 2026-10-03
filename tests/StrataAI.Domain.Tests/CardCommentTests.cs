using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CardCommentTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-03T12:00:00+02:00");
    private readonly Guid author = Guid.NewGuid();
    private CardComment Create(string content = "Original comment") => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), author, content, At);

    [Fact]
    public void PRD_15_TC_01_CommentsRetainCanonicalScopeAuthorAndPlainText()
    {
        var comment = Create("  First line\r\n<script>text</script> @board\rNext\tline 🙂  ");
        Assert.Equal(author, comment.AuthorId); Assert.NotEqual(Guid.Empty, comment.OrganizationId); Assert.NotEqual(Guid.Empty, comment.CardId);
        Assert.Equal("First line\n<script>text</script> @board\nNext\tline 🙂", comment.Content);
        Assert.Equal(TimeSpan.Zero, comment.CreatedAt.Offset); Assert.Equal(comment.CreatedAt, comment.UpdatedAt);
        Assert.Equal(1, comment.Version); Assert.False(comment.IsEdited); Assert.Null(comment.DeletedAt);
        // Markup and mention-like text remain literal; neither grants authority
        // nor creates recipients without Application admission.
    }
    [Fact]
    public void PRD_15_TC_01_ValidEditAdvancesOnlyCommentRevisionAndRetainsAuthor()
    {
        var comment = Create(); var created = comment.CreatedAt;
        Assert.True(comment.Edit(author, 1, "Revised comment", At.AddMinutes(1)));
        Assert.Equal("Revised comment", comment.Content); Assert.Equal(2, comment.Version);
        Assert.True(comment.IsEdited); Assert.Equal(comment.UpdatedAt, comment.EditedAt);
        Assert.Equal(created, comment.CreatedAt); Assert.Equal(author, comment.AuthorId);
        Assert.False(comment.Edit(author, 2, " Revised comment ", At.AddMinutes(2)));
        Assert.Equal(2, comment.Version); Assert.Equal(At.AddMinutes(1).ToUniversalTime(), comment.UpdatedAt);
    }
    [Fact]
    public void PRD_15_TC_04_ForeignAuthorCannotEditOrDeleteEvenWithCurrentRevision()
    {
        var comment = Create();
        foreach (var actor in new[] { Guid.NewGuid(), Guid.Empty })
        {
            Assert.Throws<UnauthorizedAccessException>(() => comment.Edit(actor, 1, "Foreign edit", At.AddMinutes(1)));
            Assert.Throws<UnauthorizedAccessException>(() => comment.Delete(actor, 1, At.AddMinutes(1)));
        }
        Assert.Equal("Original comment", comment.Content); Assert.Equal(1, comment.Version); Assert.Null(comment.DeletedBy);
    }
    [Fact]
    public void PRD_15_TC_08_StaleRevisionAndBackwardsTimePreserveState()
    {
        var comment = Create(); comment.Edit(author, 1, "Winner", At.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => comment.Edit(author, 1, "Stale", At.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => comment.Delete(author, 1, At.AddMinutes(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => comment.Edit(author, 2, "Winner", At));
        Assert.Throws<ArgumentOutOfRangeException>(() => comment.Delete(author, 2, At));
        Assert.Equal("Winner", comment.Content); Assert.Equal(2, comment.Version); Assert.Null(comment.DeletedAt);
    }
    [Fact]
    public void PRD_15_TC_10_DeletionRedactsBodyAndRetainsImmutableHistoricalAttribution()
    {
        var comment = Create(); comment.Edit(author, 1, "Private revised content", At.AddMinutes(1)); var edited = comment.EditedAt;
        Assert.True(comment.Delete(author, 2, At.AddMinutes(2)));
        Assert.Null(comment.Content); Assert.Equal(author, comment.AuthorId); Assert.Equal(author, comment.DeletedBy);
        Assert.Equal(edited, comment.EditedAt); Assert.True(comment.IsEdited); Assert.Equal(3, comment.Version);
        Assert.Equal(comment.UpdatedAt, comment.DeletedAt);
        Assert.False(comment.Delete(author, 3, At.AddMinutes(3))); Assert.Equal(3, comment.Version);
        Assert.Throws<InvalidOperationException>(() => comment.Edit(author, 3, "Revive", At.AddMinutes(3)));
        Assert.Throws<UnauthorizedAccessException>(() => comment.Delete(Guid.NewGuid(), 3, At.AddMinutes(3)));
        Assert.Null(comment.Content);
    }
    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    [InlineData("unsafe\0text")]
    [InlineData("unsafe\u001btext")]
    public void PRD_15_TC_03_InvalidBodyCannotCreateOrPartiallyEdit(string body)
    {
        Assert.Throws<ArgumentException>(() => Create(body)); var comment = Create();
        Assert.Throws<ArgumentException>(() => comment.Edit(author, 1, body, At.AddMinutes(1)));
        Assert.Equal("Original comment", comment.Content); Assert.Equal(1, comment.Version); Assert.Null(comment.EditedAt);
    }
    [Fact]
    public void PRD_15_TC_03_BoundedValidUnicodeAndScopeAreRequired()
    {
        Assert.Equal(CardComment.MaximumContentLength, Create(new string('x', CardComment.MaximumContentLength)).Content!.Length);
        foreach (var body in new[] { new string('x', CardComment.MaximumContentLength + 1), "invalid\ud800", "invalid\udc00", null! })
            Assert.Throws<ArgumentException>(() => Create(body));
        Assert.Throws<ArgumentException>(() => new CardComment(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), author, "Text", At));
        Assert.Throws<ArgumentException>(() => new CardComment(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), author, "Text", At));
        Assert.Throws<ArgumentException>(() => new CardComment(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, author, "Text", At));
        Assert.Throws<ArgumentException>(() => new CardComment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "Text", At));
    }
}
