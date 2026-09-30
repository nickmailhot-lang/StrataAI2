using System.Security.Cryptography;
using System.Text;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal sealed class SecureTokenService(IIdentityDeliveryTokenSigner? signer = null, IdentityDeliveryOptions? delivery = null) : ISecureTokenService
{
    public GeneratedIdentityToken GenerateForDelivery(Guid tokenId, IdentityTokenPurpose purpose, string correlationId)
    {
        if (signer is null || delivery is null) return new GeneratedIdentityToken(Generate(), null);
        return new GeneratedIdentityToken(signer.Derive(tokenId, purpose, signer.CurrentKeyId),
            new IdentityTokenDelivery(purpose, signer.CurrentKeyId, correlationId,
                delivery.SenderAddress, delivery.PublicOrigin, delivery.ProviderAccount));
    }

    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
