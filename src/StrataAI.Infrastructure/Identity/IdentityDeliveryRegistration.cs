using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Runtime;

namespace StrataAI.Infrastructure.Identity;

public static partial class IdentityDeliveryRegistration
{
    public static bool AddIdentityDeliveryTokens(this IServiceCollection services, IConfiguration configuration, RuntimeDescriptor runtime)
    {
        var flag = configuration["STRATAAI_IDENTITY_EMAIL_ENABLED"];
        if (string.IsNullOrWhiteSpace(flag) || string.Equals(flag,"false",StringComparison.OrdinalIgnoreCase)) return false;
        if (!bool.TryParse(flag,out var enabled) || !enabled) throw new InvalidOperationException("Invalid identity email enable flag.");
        if (runtime.Mode != RuntimeMode.Production) throw new InvalidOperationException("Durable identity email requires Production mode.");
        var sender = configuration["STRATAAI_IDENTITY_EMAIL_FROM"] ?? "";
        if (!MailAddress.TryCreate(sender,out var address) || address.Address != sender)
            throw new InvalidOperationException("Identity email sender must be a plain email address.");
        var origin = configuration["STRATAAI_PUBLIC_ORIGIN"] ?? "";
        if (!Uri.TryCreate(origin,UriKind.Absolute,out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Identity delivery requires an HTTPS public origin without credentials, path or query.");
        var account = configuration["STRATAAI_IDENTITY_EMAIL_ACCOUNT"] ?? "";
        if (!AccountPattern().IsMatch(account)) throw new InvalidOperationException("Identity email provider account ID is required.");
        var current = configuration["STRATAAI_IDENTITY_TOKEN_CURRENT_KEY"] ?? "";
        var keys = new Dictionary<string,string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(configuration["STRATAAI_IDENTITY_TOKEN_KEYS"] ?? "");
            foreach (var entry in document.RootElement.EnumerateObject()) keys.Add(entry.Name,entry.Value.GetString()!);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        { throw new InvalidOperationException("Identity token key ring must be a JSON object with unique string values."); }
        IdentityDeliveryTokenSigner signer;
        try { signer = new IdentityDeliveryTokenSigner(current,keys); }
        catch (ArgumentException) { throw new InvalidOperationException("Identity token key ring configuration is invalid."); }
        services.AddSingleton<IIdentityDeliveryTokenSigner>(_ => signer);
        services.AddSingleton<IInvitationDeliveryTokenSigner>(_ => signer);
        services.AddSingleton(new IdentityDeliveryOptions(sender,uri.GetLeftPart(UriPartial.Authority),account));
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,80}$",RegexOptions.CultureInvariant)]
    private static partial Regex AccountPattern();
}
