using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class IdentityTokenConsumptionRetrySecretsTests
{
    [Fact]
    public void Token_consumption_fingerprint_binds_operation_bearer_password_and_account_without_persisting_secrets()
    {
        using var secrets = new IdentityLoginRetrySecrets("v1", new Dictionary<string, string> { ["v1"] = Convert.ToBase64String(new byte[32]) });
        var user = Guid.NewGuid(); var key = Guid.NewGuid();
        Assert.True(secrets.TryConsumptionFingerprint(user, key, IdentityTokenPurpose.ResetPassword, "private-bearer", "private-password", "v1", out var original));
        foreach (var fields in new[] {
            (user, key, IdentityTokenPurpose.VerifyEmail, "private-bearer", "private-password"),
            (user, key, IdentityTokenPurpose.ResetPassword, "different-bearer", "private-password"),
            (user, key, IdentityTokenPurpose.ResetPassword, "private-bearer", "different-password"),
            (Guid.NewGuid(), key, IdentityTokenPurpose.ResetPassword, "private-bearer", "private-password"),
            (user, Guid.NewGuid(), IdentityTokenPurpose.ResetPassword, "private-bearer", "private-password"),
        }) {
            Assert.True(secrets.TryConsumptionFingerprint(fields.Item1, fields.Item2, fields.Item3, fields.Item4, fields.Item5, "v1", out var other));
            Assert.NotEqual(original, other);
        }
        Assert.True(secrets.TryRecoveryFingerprint(user, key, IdentityTokenPurpose.ResetPassword, "private-bearer", "v1", out var request));
        Assert.NotEqual(original, request);
        Assert.DoesNotContain("private-bearer", original); Assert.DoesNotContain("private-password", original);
        Assert.False(secrets.TryConsumptionFingerprint(user, key, IdentityTokenPurpose.ResetPassword, "private-bearer", "private-password", "retired", out _));
    }
}
