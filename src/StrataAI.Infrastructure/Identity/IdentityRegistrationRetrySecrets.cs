using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

public sealed partial class IdentityLoginRetrySecrets : IIdentityRegistrationRetrySecrets
{
    public bool TryDeriveVerification(Guid userId, Guid tokenId, string keyVersion, out string token)
    {
        if (userId == Guid.Empty || tokenId == Guid.Empty) { token = ""; return false; }
        var message = Encoding.UTF8.GetBytes($"strataai:registration-retry:v1\0VERIFY_EMAIL\0{keyVersion}\0{userId:N}\0{tokenId:N}");
        return TryMac(message, keyVersion, false, out token);
    }

    public bool TryRegistrationFingerprint(Guid userId, Guid key, string emailNormalized, string password,
        string displayName, string locale, string timezone, string keyVersion, out string fingerprint, string? invitationTokenHash = null)
    {
        if (userId == Guid.Empty || key == Guid.Empty) { fingerprint = ""; return false; }
        // Preserve existing self-registration receipt bytes across upgrades.
        var message = invitationTokenHash is null ? JsonSerializer.SerializeToUtf8Bytes(new {
            Purpose = "strataai:registration-retry:v1:INTENT", keyVersion, userId, key,
            emailNormalized, password, displayName, locale, timezone,
        }) : JsonSerializer.SerializeToUtf8Bytes(new {
            Purpose = "strataai:invitation-registration-retry:v1:INTENT", keyVersion, userId, key,
            emailNormalized, password, displayName, locale, timezone, invitationTokenHash,
        });
        try { return TryMac(message, keyVersion, true, out fingerprint); }
        finally { CryptographicOperations.ZeroMemory(message); }
    }
}
