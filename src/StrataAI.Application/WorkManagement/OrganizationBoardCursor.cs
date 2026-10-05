namespace StrataAI.Application.WorkManagement;

public sealed record OrganizationBoardCursorBinding(Guid OrganizationId, Guid ActorId,
    Guid MembershipId, long OrganizationVersion, Guid PermissionGeneration, long PermissionRevision);
public interface IOrganizationBoardCursorCodec
{
    string Encode(OrganizationBoardCursorBinding binding, long position);
    bool TryDecode(OrganizationBoardCursorBinding binding, string token, out long position);
}
