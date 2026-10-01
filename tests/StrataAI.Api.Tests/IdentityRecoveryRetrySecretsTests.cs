using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class IdentityRecoveryRetrySecretsTests
{
    [Fact]
    public void Recovery_intents_and_bearers_are_separated_from_each_other_and_other_identity_operations()
    {
        using var secrets = new IdentityLoginRetrySecrets("v1", new Dictionary<string, string> { ["v1"] = Convert.ToBase64String(new byte[32]) });
        var user = Guid.NewGuid(); var token = Guid.NewGuid(); var key = Guid.NewGuid();
        Assert.True(secrets.TryDeriveRecovery(user, token, IdentityTokenPurpose.ResetPassword, "v1", out var reset));
        Assert.True(secrets.TryDeriveRecovery(user, token, IdentityTokenPurpose.VerifyEmail, "v1", out var verify));
        Assert.True(secrets.TryDeriveVerification(user, token, "v1", out var registration));
        Assert.True(secrets.TryDeriveSession(user, token, "v1", out var session));
        Assert.Equal(4, new[] { reset, verify, registration, session }.Distinct().Count());
        Assert.True(secrets.TryRecoveryFingerprint(user, key, IdentityTokenPurpose.ResetPassword, "USER@EXAMPLE.TEST", "v1", out var resetIntent));
        Assert.True(secrets.TryRecoveryFingerprint(user, key, IdentityTokenPurpose.VerifyEmail, "USER@EXAMPLE.TEST", "v1", out var verifyIntent));
        Assert.NotEqual(resetIntent, verifyIntent);
        Assert.True(secrets.TryRecoveryFingerprint(Guid.NewGuid(), key, IdentityTokenPurpose.ResetPassword, "USER@EXAMPLE.TEST", "v1", out var otherUser));
        Assert.NotEqual(resetIntent, otherUser);
        Assert.False(secrets.TryDeriveRecovery(user, token, IdentityTokenPurpose.ResetPassword, "retired", out _));
        Assert.DoesNotContain("USER@EXAMPLE.TEST", resetIntent);
    }
}
