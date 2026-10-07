using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StrataAI.Application.Common;
using StrataAI.Application.Onboarding;

namespace StrataAI.Infrastructure.Onboarding;

internal sealed class DataProtectedInvitationRecipientCursorCodec(IDataProtectionProvider protection, IClock clock)
    : IInvitationRecipientCursorCodec
{
    private readonly IDataProtector _protector = protection.CreateProtector("StrataAI2.InvitationRecipientCursor", "v1");
    private sealed record Payload(int Version, InvitationRecipientCursorBinding Binding, long Position, DateTimeOffset ExpiresAt);
    public string Encode(InvitationRecipientCursorBinding binding, long position)
    {
        if (!Valid(binding) || position < 0) throw new ArgumentException("Recipient cursor is invalid.");
        return _protector.Protect(JsonSerializer.Serialize(new Payload(1, binding, position, clock.UtcNow.ToUniversalTime().AddMinutes(15))));
    }
    public bool TryDecode(InvitationRecipientCursorBinding binding, string token, out long position)
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
    private static bool Valid(InvitationRecipientCursorBinding binding) => binding.ActorId != Guid.Empty && binding.AccountVersion > 0
        && !string.IsNullOrWhiteSpace(binding.EmailNormalized) && binding.EmailNormalized.Length <= 320
        && binding.EmailNormalized == binding.EmailNormalized.Trim().ToUpperInvariant();
}
