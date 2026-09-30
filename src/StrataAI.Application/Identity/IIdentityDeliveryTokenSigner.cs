namespace StrataAI.Application.Identity;

public enum IdentityTokenPurpose { VerifyEmail, ResetPassword }

public interface IIdentityDeliveryTokenSigner
{
    string CurrentKeyId { get; }
    string Derive(Guid tokenId, IdentityTokenPurpose purpose, string keyId);
}
