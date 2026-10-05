namespace StrataAI.Application.WorkManagement;

public enum OrganizationBoardAudience { ArchiveAdministration, BoardDiscovery }
public sealed record OrganizationBoardCursorBinding(Guid OrganizationId, Guid ActorId,
    Guid MembershipId, long OrganizationVersion, Guid PermissionGeneration, long PermissionRevision,
    OrganizationBoardAudience Audience = OrganizationBoardAudience.ArchiveAdministration, long ReaderRevision = 0);
public interface IOrganizationBoardCursorCodec
{
    string Encode(OrganizationBoardCursorBinding binding, long position);
    bool TryDecode(OrganizationBoardCursorBinding binding, string token, out long position);
}
