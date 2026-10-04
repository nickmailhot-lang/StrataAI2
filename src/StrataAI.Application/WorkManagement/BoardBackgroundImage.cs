using System.Text.Json.Serialization;

namespace StrataAI.Application.WorkManagement;

// Immutable Board ownership of already published, sanitized private PNG bytes.
// A copied Board receives its own identity; immutable bytes may have many owners.
public sealed record BoardBackgroundImage(Guid Id, Guid OrganizationId, Guid BoardId, Guid CreatedBy,
    DateTimeOffset CreatedAt, [property: JsonIgnore] AttachmentPublishedPreview Preview,
    [property: JsonIgnore] Guid? SourceImageId = null);

public sealed record SelectBoardBackgroundImageInput(Guid CardId, Guid AttachmentId, long AttachmentVersion,
    long BoardVersion, bool PublicVisibilityConfirmed = false);
