namespace StrataAI.Api.Onboarding;

public sealed record CreateInvitationRequest(
    string Email,
    string Surface,
    string TargetRole);

public sealed record CreateInvitationResponse(
    Guid Id,
    Guid OrganizationId,
    string Email,
    string Surface,
    string TargetRole,
    DateTimeOffset ExpiresAt,
    string? InvitationToken);

public sealed record AcceptInvitationRequest(string? Token);

public sealed record AcceptInvitationResponse(
    Guid InvitationId,
    Guid OrganizationId,
    string Surface,
    string TargetRole,
    StrataAI.Application.Onboarding.BoardInvitationTarget? BoardTarget = null);

public sealed record CreateBoardInvitationRequest(string Email, string Role);
