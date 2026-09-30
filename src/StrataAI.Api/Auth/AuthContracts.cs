using StrataAI.Application.Identity;

namespace StrataAI.Api.Auth;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    string? Locale,
    string? Timezone);

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record ForgotPasswordRequest(
    string Email);

public sealed record ResetPasswordRequest(
    string Token,
    string NewPassword);

public sealed record VerifyEmailRequest(
    string Token);

public sealed record UpdateProfileRequest(
    string? DisplayName,
    string? AvatarUrl,
    string? Locale,
    string? Timezone);

public sealed record RegistrationResponse(
    UserProfile User,
    string? VerificationToken);

public sealed record LoginResponse(
    UserProfile User,
    DateTimeOffset SessionExpiresAt);

public sealed record PasswordResetRequestResponse(
    bool Accepted,
    string? ResetToken);
