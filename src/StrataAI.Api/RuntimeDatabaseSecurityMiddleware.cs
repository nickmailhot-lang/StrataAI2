using StrataAI.Application.Runtime;

namespace StrataAI.Api;

public sealed class RuntimeDatabaseSecurityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ILogger<RuntimeDatabaseSecurityMiddleware> logger)
    {
        try { await next(context); }
        catch (Exception exception) when (exception is RuntimeDatabaseRoleException or RuntimeDatabaseSchemaException && !context.Response.HasStarted)
        {
            logger.LogWarning("Unsafe or missing runtime database security configuration. CorrelationId={CorrelationId}", context.TraceIdentifier);
            await Results.Problem(statusCode:503,title:"The service is temporarily unavailable.",
                extensions:new Dictionary<string,object?> { ["code"]=exception is RuntimeDatabaseSchemaException ? "runtime_database_schema_incompatible" : "runtime_database_role_unsafe" }).ExecuteAsync(context);
        }
    }
}
