using System.Text;
using System.Text.Json;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

public sealed partial class IdentityLoginRetrySecrets : IIdentityRecoveryRetrySecrets
{
    public bool TryDeriveRecovery(Guid userId, Guid tokenId, IdentityTokenPurpose purpose, string version, out string token)
    {
        if (userId == Guid.Empty || tokenId == Guid.Empty || !Enum.IsDefined(purpose)) { token = ""; return false; }
        return TryMac(Encoding.UTF8.GetBytes($"strataai:recovery-request:v1\0TOKEN\0{purpose}\0{version}\0{userId:N}\0{tokenId:N}"), version, false, out token);
    }
    public bool TryRecoveryFingerprint(Guid userId, Guid key, IdentityTokenPurpose purpose, string emailNormalized, string version, out string fingerprint)
    {
        if (userId == Guid.Empty || key == Guid.Empty || !Enum.IsDefined(purpose)) { fingerprint = ""; return false; }
        return TryMac(JsonSerializer.SerializeToUtf8Bytes(new { Purpose = "strataai:recovery-request:v1:INTENT", operation = purpose.ToString(), version, userId, key, emailNormalized }), version, true, out fingerprint);
    }
}
