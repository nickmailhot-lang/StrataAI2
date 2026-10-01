using Microsoft.Extensions.Configuration;
using StrataAI.Application.Runtime;

namespace StrataAI.Infrastructure.Onboarding;

public static class InvitationMailRegistration
{
    public static bool IsEnabled(IConfiguration configuration, RuntimeDescriptor runtime)
    {
        var value = configuration["STRATAAI_INVITATION_EMAIL_ENABLED"];
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!bool.TryParse(value, out var enabled)) throw new InvalidOperationException("Invalid invitation email enable flag.");
        if (!enabled) return false;
        if (runtime.Mode != RuntimeMode.Production || !bool.TryParse(configuration["STRATAAI_IDENTITY_EMAIL_ENABLED"], out var identityEnabled) || !identityEnabled)
            throw new InvalidOperationException("Invitation email requires Production mode and configured identity email transport.");
        return true;
    }
}
