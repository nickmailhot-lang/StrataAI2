using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class DataProtectedOrganizationBoardCursorCodec(IDataProtectionProvider protection, IClock clock)
    : IOrganizationBoardCursorCodec
{
    private readonly IDataProtector _protector = protection.CreateProtector("StrataAI2.OrganizationBoardCursor", "v1");
    private sealed record Payload(int Version, OrganizationBoardCursorBinding Binding, long Position, DateTimeOffset ExpiresAt);
    public string Encode(OrganizationBoardCursorBinding binding, long position)
    {
        if (!Valid(binding) || position < 0) throw new ArgumentException("Organization Board cursor is invalid.");
        return _protector.Protect(JsonSerializer.Serialize(new Payload(1, binding, position, clock.UtcNow.ToUniversalTime().AddMinutes(15))));
    }
    public bool TryDecode(OrganizationBoardCursorBinding binding, string token, out long position)
    {
        position = 0;
        if (!Valid(binding) || string.IsNullOrEmpty(token) || token.Length > 4096) return false;
        try
        {
            var value = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
            if (value is null || value.Version != 1 || value.Binding != binding || value.Position < 0 ||
                value.ExpiresAt.Offset != TimeSpan.Zero || value.ExpiresAt <= clock.UtcNow) return false;
            position = value.Position;
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        { return false; }
    }
    private static bool Valid(OrganizationBoardCursorBinding? binding) => binding is not null &&
        binding.OrganizationId != Guid.Empty && binding.ActorId != Guid.Empty && binding.MembershipId != Guid.Empty &&
        binding.OrganizationVersion > 0 && binding.PermissionGeneration != Guid.Empty && binding.PermissionRevision > 0 &&
        Enum.IsDefined(binding.Audience) && (binding.Audience == OrganizationBoardAudience.ArchiveAdministration
            ? binding.ReaderRevision == 0 : binding.ReaderRevision > 0);
}
