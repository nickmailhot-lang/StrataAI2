namespace StrataAI.Application.Identity;

public interface ISecureTokenService
{
    string Generate();

    string Hash(string rawToken);

    GeneratedIdentityToken GenerateForDelivery(Guid tokenId, IdentityTokenPurpose purpose, string correlationId);
}

public sealed record GeneratedIdentityToken(string RawToken, IdentityTokenDelivery? Delivery);
public sealed record IdentityTokenDelivery(IdentityTokenPurpose Purpose, string KeyId, string CorrelationId,
    string SenderAddress, string PublicOrigin, string ProviderAccount);
public sealed record IdentityDeliveryOptions(string SenderAddress, string PublicOrigin, string ProviderAccount);
