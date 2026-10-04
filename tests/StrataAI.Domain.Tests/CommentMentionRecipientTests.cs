using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CommentMentionRecipientTests
{
    [Fact]
    public void PRD_15_EditDeltaUsesStableRecipientsPreservesReferencesAndSuppressesSelf()
    {
        var actor = Guid.NewGuid(); var retained = Guid.NewGuid(); var added = Guid.NewGuid();
        var mapping = new Dictionary<string, Guid> { ["self_user"] = actor, ["renamed_user"] = retained, ["new_user"] = added };
        var previous = new List<Guid> { retained };
        var text = CommentMentionText.Parse("  🙂 @self_user @RENAMED_USER @new_user @new_user @former_user  ");
        var plan = CommentMentionRecipients.Capture(text, actor, mapping, previous);
        Assert.Equal(4, plan.References.Count); Assert.Equal(3, plan.Current.Count); Assert.Equal(added, Assert.Single(plan.Added));
        Assert.All(plan.References, reference => Assert.Equal("@" + reference.Handle, text.Content.Substring(reference.Start, reference.Length).ToLowerInvariant()));
        mapping.Clear(); previous.Clear(); Assert.Equal(3, plan.Current.Count); Assert.Equal(added, Assert.Single(plan.Added));
        Assert.Empty(CommentMentionRecipients.Capture(text, actor, new Dictionary<string, Guid>
            { ["self_user"] = actor, ["renamed_user"] = retained, ["new_user"] = added }, plan.Current).Added);
        var removed = CommentMentionRecipients.Capture(CommentMentionText.Parse("Plain comment"), actor, new Dictionary<string, Guid>(), plan.Current);
        Assert.Empty(removed.Current); Assert.Empty(removed.Added);
        // Readding after an intervening revision without this recipient is new.
        Assert.Equal(added, Assert.Single(CommentMentionRecipients.Capture(CommentMentionText.Parse("@new_user"), actor,
            new Dictionary<string, Guid> { ["new_user"] = added }, removed.Current).Added));
    }
    [Fact]
    public void PRD_15_UsernameRecipientsAreBoundedWhileCompletePriorGroupHistoryRetainsStableIdentity()
    {
        var actor = Guid.NewGuid(); var ids = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray();
        var mapping = ids.Select((id, n) => new KeyValuePair<string, Guid>("member_" + n, id)).ToDictionary();
        var text = CommentMentionText.Parse(string.Join(' ', mapping.Keys.Select(handle => "@" + handle)));
        Assert.Throws<ArgumentException>(() => CommentMentionRecipients.Capture(text, actor, mapping, []));
        mapping.Remove("member_20"); Assert.Equal(20, CommentMentionRecipients.Capture(text, actor, mapping, []).Current.Count);
        Assert.Empty(CommentMentionRecipients.Capture(text, actor, mapping, ids).Added);
        foreach (var bad in new[] { new[] { Guid.Empty }, new[] { ids[0], ids[0] } })
            Assert.Throws<ArgumentException>(() => CommentMentionRecipients.Capture(text, actor, mapping, bad));
        Assert.Throws<ArgumentException>(() => CommentMentionRecipients.Capture(text, Guid.Empty, mapping, []));
        var declarations = CommentMentionText.Parse("@board @card @unknown_user x@example.test https://example.test/@member_1");
        var unbound = CommentMentionRecipients.Capture(declarations, actor, mapping, []);
        Assert.Empty(unbound.Current); Assert.Empty(unbound.Added); Assert.Empty(unbound.References);
    }
}
