using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class IdentityRegistrationRetrySecretsTests
{
    [Fact]
    public void Registration_proofs_are_separated_from_sign_in_and_bound_to_every_submitted_field()
    {
        using var secrets = new IdentityLoginRetrySecrets("v1", new Dictionary<string, string> { ["v1"] = Convert.ToBase64String(new byte[32]) });
        var user = Guid.NewGuid(); var tokenId = Guid.NewGuid(); var key = Guid.NewGuid();
        Assert.True(secrets.TryDeriveVerification(user, tokenId, "v1", out var verification));
        Assert.True(secrets.TryDeriveSession(user, tokenId, "v1", out var session));
        Assert.NotEqual(session, verification);
        Assert.True(secrets.TryDeriveVerification(user, tokenId, "v1", out var repeated)); Assert.Equal(verification, repeated);
        Assert.False(secrets.TryDeriveVerification(user, tokenId, "retired", out _));
        Assert.True(secrets.TryRegistrationFingerprint(user, key, "PERSON@EXAMPLE.TEST", "private-password", "Person", "en-CA", "UTC", "v1", out var original));
        Assert.True(secrets.TryFingerprint(user, key, "PERSON@EXAMPLE.TEST", "private-password", "v1", out var login)); Assert.NotEqual(original, login);
        var variations = new[] {
            new[] { "OTHER@EXAMPLE.TEST", "private-password", "Person", "en-CA", "UTC" },
            new[] { "PERSON@EXAMPLE.TEST", "changed-password", "Person", "en-CA", "UTC" },
            new[] { "PERSON@EXAMPLE.TEST", "private-password", "Changed", "en-CA", "UTC" },
            new[] { "PERSON@EXAMPLE.TEST", "private-password", "Person", "fr-CA", "UTC" },
            new[] { "PERSON@EXAMPLE.TEST", "private-password", "Person", "en-CA", "America/Vancouver" },
        };
        foreach (var fields in variations)
        {
            Assert.True(secrets.TryRegistrationFingerprint(user, key, fields[0], fields[1], fields[2], fields[3], fields[4], "v1", out var changed));
            Assert.NotEqual(original, changed);
        }
        Assert.DoesNotContain("private-password", original);
    }
}
