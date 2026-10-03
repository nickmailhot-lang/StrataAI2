namespace StrataAI.Application.WorkManagement;

public interface ICardAttachmentCoverStore
{
    Task<Guid?> FindSelectedAsync(Guid organization, Guid card, CancellationToken ct);
    // Owning transaction and current authorization belong to the caller.
    // Holds Card then source locks and compares both revisions before changing.
    Task<CardRecord?> SetAsync(Guid organization, Guid board, Guid card, Guid? attachment,
        long? sourceVersion, Guid? previous, long cardVersion, DateTimeOffset now, CancellationToken ct);
}
public sealed record SetCardCoverInput(Guid? AttachmentId, long CardVersion, long? AttachmentVersion, bool PublicVisibilityConfirmed = false);
public sealed record CardCoverView(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    Guid? AttachmentId, long? AttachmentVersion, bool CanEdit, bool IsPublic);
public sealed record CardCoverChange(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    Guid? AttachmentId, long? AttachmentVersion, bool Changed);
