namespace StrataAI.Application.Identity;

public interface IPasswordHashService
{
    string Hash(Guid userId, string password);

    PasswordVerification Verify(
        Guid userId,
        string passwordHash,
        string providedPassword);
}

public sealed record PasswordVerification(
    bool IsValid,
    bool NeedsRehash);
