using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class DataProtectedGlobalSearchCursorCodec(IDataProtectionProvider protection, IClock clock) : IGlobalSearchCursorCodec
{
    private readonly IDataProtector _protector = protection.CreateProtector("StrataAI2.GlobalSearchCursor", "v1");
    private sealed record Payload(int Version, GlobalSearchBinding Binding, GlobalSearchPosition Position, DateTimeOffset ExpiresAt);
    public string Encode(GlobalSearchBinding binding, GlobalSearchPosition position)
    {
        if (!Valid(binding) || !Valid(position)) throw new ArgumentException("Global search cursor is invalid.");
        return _protector.Protect(JsonSerializer.Serialize(new Payload(1, binding, position, clock.UtcNow.ToUniversalTime().AddMinutes(15))));
    }
    public bool TryDecode(GlobalSearchBinding binding, string token, out GlobalSearchPosition? position)
    {
        position = null;
        // Three 160-UTF16-unit criteria may expand to six-byte JSON escapes
        // before encryption/base64. The cap must admit every valid binding.
        if (!Valid(binding) || string.IsNullOrEmpty(token) || token.Length > 8192) return false;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
            if (payload is null || payload.Version != 1 || payload.Binding != binding || !Valid(payload.Position)
                || payload.ExpiresAt.Offset != TimeSpan.Zero || payload.ExpiresAt <= clock.UtcNow) return false;
            position = payload.Position; return true;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        { return false; }
    }
    private static bool Text(string? value) => value is not null && value.Length <= 160 && value == value.Trim();
    private static bool Valid(GlobalSearchBinding? binding) => binding is not null && binding.ActorId != Guid.Empty
        && Text(binding.Keyword) && Text(binding.Label) && Text(binding.Member) && Enum.IsDefined(binding.Scope);
    private static bool Valid(GlobalSearchPosition? position) => position is not null && position.OrganizationId != Guid.Empty
        && position.BoardId != Guid.Empty && position.CardId != Guid.Empty && (position.CardId is null || position.BoardId is not null);
}
