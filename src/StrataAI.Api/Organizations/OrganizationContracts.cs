namespace StrataAI.Api.Organizations;

public sealed record LeaveOrganizationRequest(Guid? ExpectedActorId);

public sealed record CreateOrganizationRequest(
    string Name,
    string? Description,
    string? Type = null);

public sealed record UpdateOrganizationRequest(
    string Name,
    string? Description,
    string? LogoUrl,
    long Version);
