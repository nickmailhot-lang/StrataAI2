using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class InvitationDeliveryTokenSignerTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
    private static readonly string NewKey = Convert.ToBase64String(Enumerable.Range(33, 32).Select(x => (byte)x).ToArray());

    [Fact]
    public void Invitation_proof_reconstructs_after_restart_and_old_key_rotation_without_persisting_bearer()
    {
        var organization = Guid.NewGuid(); var invitation = Guid.NewGuid();
        using var api = new IdentityDeliveryTokenSigner("old", new Dictionary<string, string> { ["old"] = Key });
        var token = api.DeriveInvitation(organization, invitation, api.CurrentKeyId);
        using var worker = new IdentityDeliveryTokenSigner("new", new Dictionary<string, string> { ["old"] = Key, ["new"] = NewKey });
        Assert.Equal(token, worker.DeriveInvitation(organization, invitation, "old"));
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        Assert.NotEqual(token, worker.DeriveInvitation(organization, invitation, "new"));
        Assert.DoesNotContain(organization.ToString("N"), token, StringComparison.Ordinal);
        Assert.DoesNotContain(invitation.ToString("N"), token, StringComparison.Ordinal);
    }

    [Fact]
    public void Invitation_tenant_identity_and_purpose_are_not_interchangeable_even_with_identical_key_aliases()
    {
        using var signer = new IdentityDeliveryTokenSigner("key", new Dictionary<string, string> { ["key"] = Key, ["alias"] = Key });
        var organization = Guid.NewGuid(); var invitation = Guid.NewGuid();
        var token = signer.DeriveInvitation(organization, invitation, "key");
        Assert.NotEqual(token, signer.DeriveInvitation(Guid.NewGuid(), invitation, "key"));
        Assert.NotEqual(token, signer.DeriveInvitation(organization, Guid.NewGuid(), "key"));
        Assert.NotEqual(token, signer.DeriveInvitation(organization, invitation, "alias"));
        Assert.NotEqual(token, signer.Derive(invitation, IdentityTokenPurpose.VerifyEmail, "key"));
        Assert.NotEqual(token, signer.Derive(invitation, IdentityTokenPurpose.ResetPassword, "key"));
    }

    [Fact]
    public void Missing_key_empty_scope_and_disposal_fail_closed_without_secret_disclosure()
    {
        using var signer = new IdentityDeliveryTokenSigner("key", new Dictionary<string, string> { ["key"] = Key });
        var error = Assert.Throws<InvalidOperationException>(() => signer.DeriveInvitation(Guid.NewGuid(), Guid.NewGuid(), "missing"));
        Assert.DoesNotContain(Key, error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => signer.DeriveInvitation(Guid.Empty, Guid.NewGuid(), "key"));
        Assert.Throws<ArgumentException>(() => signer.DeriveInvitation(Guid.NewGuid(), Guid.Empty, "key"));
        signer.Dispose(); signer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => signer.DeriveInvitation(Guid.NewGuid(), Guid.NewGuid(), "key"));
    }
}
