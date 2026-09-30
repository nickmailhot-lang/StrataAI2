using Microsoft.AspNetCore.Identity;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal sealed class AspNetPasswordHashService : IPasswordHashService
{
    private readonly PasswordHasher<Guid> _hasher = new();

    public string Hash(Guid userId, string password) =>
        _hasher.HashPassword(userId, password);

    public PasswordVerification Verify(
        Guid userId,
        string passwordHash,
        string providedPassword)
    {
        var result = _hasher.VerifyHashedPassword(
            userId,
            passwordHash,
            providedPassword);

        return new PasswordVerification(
            result is PasswordVerificationResult.Success
                or PasswordVerificationResult.SuccessRehashNeeded,
            result == PasswordVerificationResult.SuccessRehashNeeded);
    }
}
