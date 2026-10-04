using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CommentMentionRecipientTests
{
    [Fact]
    public void PRD_15_ConfirmedGroupsKeepCompleteStableUnionAndOnlyNewNonSelfDelivery()
    {
        var actor = Guid.NewGuid(); var members = Enumerable.Range(0, 75).Select(_ => Guid.NewGuid()).ToList();
        var card = new List<Guid> { actor, members[0] }; var board = members.Append(actor).ToList();
        var previous = members.Take(70).ToList(); var mapping = new Dictionary<string, Guid> { ["named_user"] = members[0] };
        var text = CommentMentionText.Parse("@card @board @card @named_user @named_user");
        var plan = CommentMentionRecipients.CaptureConfirmedGroups(text, actor, mapping, previous, true, true, card, board);
        Assert.Equal(76, plan.Current.Count); Assert.Equal(2, plan.References.Count);
        Assert.Equal(members.Skip(70).Order(), plan.Added); Assert.Contains(actor, plan.Current); Assert.DoesNotContain(actor, plan.Added);
        card.Clear(); board.Clear(); mapping.Clear(); previous.Clear(); members.Clear();
        Assert.Equal(76, plan.Current.Count); Assert.Equal(5, plan.Added.Count);
        Assert.Empty(CommentMentionRecipients.CaptureConfirmedGroups(text, actor, new Dictionary<string, Guid>(), plan.Current,
            true, true, [], plan.Current).Added);
        var removed = CommentMentionRecipients.Capture(CommentMentionText.Parse("Plain text"), actor, new Dictionary<string, Guid>(), plan.Current);
        Assert.Empty(removed.Current);
        Assert.Equal(75, CommentMentionRecipients.CaptureConfirmedGroups(text, actor, new Dictionary<string, Guid>(), removed.Current,
            true, true, [], plan.Current).Added.Count);
    }
    [Fact]
    public void PRD_15_GroupConfirmationRequiresActualDeclarationAndCanonicalScopedRosters()
    {
        var actor = Guid.NewGuid(); var target = Guid.NewGuid(); var handles = new Dictionary<string, Guid>();
        var text = CommentMentionText.Parse("@card @board");
        Assert.Empty(CommentMentionRecipients.Capture(text, actor, handles, []).Current);
        Assert.Empty(CommentMentionRecipients.CaptureConfirmedGroups(text, actor, handles, [], true, true, [], []).Current);
        Assert.Throws<ArgumentException>(() => CommentMentionRecipients.CaptureConfirmedGroups(text, actor, handles, [], false, true, [target], []));
        Assert.Throws<ArgumentException>(() => CommentMentionRecipients.CaptureConfirmedGroups(text, actor, handles, [], true, false, [], [target]));
        foreach (var invalid in new[] { new[] { Guid.Empty }, new[] { target, target } })
            Assert.Throws<ArgumentException>(() => CommentMentionRecipients.CaptureConfirmedGroups(text, actor, handles, [], true, true, [], invalid));
        foreach (var literal in new[] { "Plain text", "x@board", "https://example.test/@board", "@board_extra" })
            Assert.Throws<ArgumentException>(() => CommentMentionRecipients.CaptureConfirmedGroups(CommentMentionText.Parse(literal), actor,
                handles, [], false, true, [], [target]));
        var named = Enumerable.Range(0, 21).ToDictionary(n => "user_" + n, _ => Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => CommentMentionRecipients.CaptureConfirmedGroups(
            CommentMentionText.Parse("@board " + string.Join(' ', named.Keys.Select(name => "@" + name))), actor, named, [], false, true, [], [target]));
    }
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
