namespace StrataAI.Api.Organizations;

public sealed record CreateOrganizationRequest(
    string Name,
    string? Description);

public sealed record UpdateOrganizationRequest(
    string Name,
    string? Description,
    string? LogoUrl,
    long Version);
