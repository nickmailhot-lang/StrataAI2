using Microsoft.AspNetCore.Identity;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal sealed class AspNetPasswordHashService : IPasswordHashService
{
    private sealed record PasswordHashSubject(Guid Id);

    private readonly PasswordHasher<PasswordHashSubject> _hasher = new();

    public string Hash(Guid userId, string password) =>
        _hasher.HashPassword(new PasswordHashSubject(userId), password);

    public PasswordVerification Verify(
        Guid userId,
        string passwordHash,
        string providedPassword)
    {
        var result = _hasher.VerifyHashedPassword(
            new PasswordHashSubject(userId),
            passwordHash,
            providedPassword);

        return new PasswordVerification(
            result is PasswordVerificationResult.Success
                or PasswordVerificationResult.SuccessRehashNeeded,
            result == PasswordVerificationResult.SuccessRehashNeeded);
    }
}
