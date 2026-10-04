using Microsoft.AspNetCore.DataProtection;

namespace StrataAI.Api.WorkManagement;

public static class ActivityCursorKeyRegistration
{
    public const string ApplicationName = "StrataAI2.InternalActivity";
    public static void AddStrataAiActivityKeys(this IServiceCollection services, IConfiguration configuration)
    {
        var keys = services.AddDataProtection().SetApplicationName(ApplicationName);
        var directory = configuration["STRATAAI_ACTIVITY_KEY_DIRECTORY"];
        if (directory is null) return;
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("Activity key storage requires an absolute directory.");
        // Deployment owns the protected volume; never place key material in
        // the served web tree or exported release artifacts. No Worker access.
        keys.PersistKeysToFileSystem(new DirectoryInfo(directory));
    }
}
