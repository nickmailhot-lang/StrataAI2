using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class DataProtectedOrganizationConfigurationActivityCursorCodec(IDataProtectionProvider protection, IClock clock)
    : IOrganizationConfigurationActivityCursorCodec
{
    private readonly IDataProtector protector = protection.CreateProtector("StrataAI2.OrganizationConfigurationActivityCursor", "v1");
    private sealed record Payload(int Version, OrganizationConfigurationCursorBinding Binding, long BeforeVersion, DateTimeOffset ExpiresAt);
    public string Encode(OrganizationConfigurationCursorBinding binding, long beforeVersion)
    {
        if (!Valid(binding) || beforeVersion < 1) throw new ArgumentException("Configuration Activity cursor is invalid.");
        return protector.Protect(JsonSerializer.Serialize(new Payload(1, binding, beforeVersion,
            clock.UtcNow.ToUniversalTime().AddMinutes(15))));
    }
    public bool TryDecode(OrganizationConfigurationCursorBinding binding, string token, out long beforeVersion)
    {
        beforeVersion = 0;
        if (!Valid(binding) || string.IsNullOrEmpty(token) || token.Length > 4096) return false;
        try
        {
            var value = JsonSerializer.Deserialize<Payload>(protector.Unprotect(token));
            if (value is null || value.Version != 1 || value.Binding != binding || value.BeforeVersion < 1
                || value.ExpiresAt.Offset != TimeSpan.Zero || value.ExpiresAt <= clock.UtcNow) return false;
            beforeVersion = value.BeforeVersion; return true;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException) { return false; }
    }
    private static bool Valid(OrganizationConfigurationCursorBinding? binding) => binding is not null
        && binding.OrganizationId != Guid.Empty && binding.ActorId != Guid.Empty && binding.MembershipId != Guid.Empty
        && binding.MembershipVersion > 0 && binding.Role is OrganizationRole.Owner or OrganizationRole.Admin;
}
