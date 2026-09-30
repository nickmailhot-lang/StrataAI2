namespace StrataAI.Application.Identity;

public interface ISecureTokenService
{
    string Generate();

    string Hash(string rawToken);
}
