using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CommentMentionsTests
{
    [Fact]
    public void PRD_15_TC_03_HandleNamespaceIsCanonicalUnambiguousAndReservesMassAndGeneratedNames()
    {
        Assert.Equal("nick_123", MentionHandle.RequireCustom(" Nick_123 "));
        Assert.Equal(new string('a', 40), MentionHandle.RequireCustom(new string('a', 40)));
        var account = Guid.NewGuid(); Assert.Equal($"u_{account:N}", MentionHandle.Normalize(MentionHandle.DefaultForUser(account)));
        Assert.NotEqual(MentionHandle.DefaultForUser(account), MentionHandle.DefaultForUser(Guid.NewGuid()));
        foreach (var value in new[] { "", "ab", "1abc", "CARD", "board", "u_owner", "Nick.Name", "a-b", "äname", "a\0b", new string('a', 41) })
            Assert.Throws<ArgumentException>(() => MentionHandle.RequireCustom(value));
        Assert.Throws<ArgumentException>(() => MentionHandle.DefaultForUser(Guid.Empty));
    }
    [Fact]
    public void PRD_15_TC_01_NormalizedUnicodeBodyRetainsExactUtf16ReferencesAndDistinctMassDeclarations()
    {
        var parsed = CommentMentionText.Parse("  🙂\r\n@Nick_123, (@CARD) [@board] @nick_123.  ");
        Assert.Equal("🙂\n@Nick_123, (@CARD) [@board] @nick_123.", parsed.Content);
        Assert.Equal(4, parsed.Tokens.Count);
        Assert.Equal(new[] { CommentMentionKind.User, CommentMentionKind.Card, CommentMentionKind.Board, CommentMentionKind.User }, parsed.Tokens.Select(t => t.Kind));
        Assert.Equal(3, parsed.Tokens[0].Start); Assert.Equal("nick_123", parsed.Tokens[0].Handle);
        foreach (var token in parsed.Tokens) Assert.Equal("@" + token.Handle, parsed.Content.Substring(token.Start, token.Length).ToLowerInvariant());
        var account = Guid.NewGuid(); var bound = parsed.BindUsers(new Dictionary<string, Guid> { ["nick_123"] = account });
        Assert.Equal(2, bound.Count); Assert.All(bound, mention => Assert.Equal(account, mention.UserId));
        Assert.Equal(parsed.Tokens.Where(t => t.Kind == CommentMentionKind.User).Select(t => t.Start), bound.Select(t => t.Start));
    }
    [Theory]
    [InlineData("nick@example.test https://example.test/@nick /@nick \\@nick")]
    [InlineData("@nick.example @nick-other @nickä @nick🙂 @nick@else")]
    [InlineData("@ab @1name @äname")]
    [InlineData("https://example.test?@nick https://example.test?q=one,@nick MAILTO:@nick www.example.test?@nick")]
    public void PRD_15_TC_03_ReferencesCannotBeInferredFromEmailsUrlsEscapesOrPartialUnsupportedNames(string content)
        => Assert.Empty(CommentMentionText.Parse(content).Tokens);
    [Fact]
    public void PRD_15_TC_04_OnlyExplicitAuthorizedHandleMapBindsUsersAndNeverProducesMassRecipients()
    {
        var parsed = CommentMentionText.Parse("@allowed @unknown @card @board"); var account = Guid.NewGuid();
        var bound = parsed.BindUsers(new Dictionary<string, Guid> { ["allowed"] = account });
        Assert.Equal(account, Assert.Single(bound).UserId); Assert.Equal("allowed", bound[0].Handle);
        Assert.Empty(parsed.BindUsers(new Dictionary<string, Guid>()));
        Assert.Throws<ArgumentException>(() => parsed.BindUsers(new Dictionary<string, Guid> { ["Allowed"] = account }));
        Assert.Throws<ArgumentException>(() => parsed.BindUsers(new Dictionary<string, Guid> { ["allowed"] = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => parsed.BindUsers(new Dictionary<string, Guid> { ["board"] = account }));
        Assert.Throws<ArgumentException>(() => parsed.BindUsers(Enumerable.Range(0, 65).ToDictionary(n => $"user{n}", _ => Guid.NewGuid())));
    }
    [Fact]
    public void PRD_15_TC_03_TokenAndDistinctUserBoundsRefuseOverflowWithoutTruncatingReferences()
    {
        var sixtyFour = string.Join(' ', Enumerable.Repeat("@nick", 64));
        var parsed = CommentMentionText.Parse(sixtyFour); var account = Guid.NewGuid();
        Assert.Equal(64, parsed.Tokens.Count); Assert.Equal(64, parsed.BindUsers(new Dictionary<string, Guid> { ["nick"] = account }).Count);
        Assert.Throws<ArgumentException>(() => CommentMentionText.Parse(sixtyFour + " @nick"));
        var handles = Enumerable.Range(0, 21).ToDictionary(n => $"user{n}", _ => Guid.NewGuid());
        var body = string.Join(' ', handles.Keys.Select(h => "@" + h));
        Assert.Throws<ArgumentException>(() => CommentMentionText.Parse(body).BindUsers(handles));
        Assert.Equal(20, CommentMentionText.Parse(string.Join(' ', handles.Keys.Take(20).Select(h => "@" + h))).BindUsers(handles).Count);
    }
}
