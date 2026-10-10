using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class DataProtectedOrganizationConfigurationCursorCodec(IDataProtectionProvider protection, IClock clock)
    : IOrganizationConfigurationCursorCodec
{
    private readonly IDataProtector _protector = protection.CreateProtector("StrataAI2.OrganizationConfigurationCursor", "v1");
    private sealed record Payload(int Version, OrganizationConfigurationCursorBinding Binding, long Position, DateTimeOffset ExpiresAt);
    public string Encode(OrganizationConfigurationCursorBinding binding, long position)
    {
        if (!Valid(binding) || position < 0) throw new ArgumentException("Configuration cursor is invalid.");
        return _protector.Protect(JsonSerializer.Serialize(new Payload(1, binding, position,
            clock.UtcNow.ToUniversalTime().AddMinutes(15))));
    }
    public bool TryDecode(OrganizationConfigurationCursorBinding binding, string token, out long position)
    {
        position = 0;
        if (!Valid(binding) || string.IsNullOrEmpty(token) || token.Length > 4096) return false;
        try
        {
            var value = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
            if (value is null || value.Version != 1 || value.Binding != binding || value.Position < 0
                || value.ExpiresAt.Offset != TimeSpan.Zero || value.ExpiresAt <= clock.UtcNow) return false;
            position = value.Position; return true;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException) { return false; }
    }
    private static bool Valid(OrganizationConfigurationCursorBinding? binding) => binding is not null
        && binding.OrganizationId != Guid.Empty && binding.ActorId != Guid.Empty && binding.MembershipId != Guid.Empty
        && binding.MembershipVersion > 0 && binding.Role is OrganizationRole.Owner or OrganizationRole.Admin;
}
