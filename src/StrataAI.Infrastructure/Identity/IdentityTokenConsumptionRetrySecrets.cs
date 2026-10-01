using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

public sealed partial class IdentityLoginRetrySecrets : IIdentityTokenConsumptionRetrySecrets
{
    public bool TryConsumptionFingerprint(Guid userId, Guid key, IdentityTokenPurpose purpose, string rawToken, string? newPassword, string version, out string fingerprint)
    {
        if (userId == Guid.Empty || key == Guid.Empty || !Enum.IsDefined(purpose)) { fingerprint = ""; return false; }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Purpose = "strataai:token-consumption:v1:INTENT", operation = purpose.ToString(), version, userId, key, rawToken, newPassword });
        try { return TryMac(bytes, version, true, out fingerprint); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
