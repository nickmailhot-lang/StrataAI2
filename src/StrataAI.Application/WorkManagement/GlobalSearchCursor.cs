namespace StrataAI.Application.WorkManagement;

public enum GlobalSearchLifecycleScope { Active, Archived }
public sealed record GlobalSearchBinding(Guid ActorId, string Keyword, string Label, string Member,
    bool MatchAll, GlobalSearchLifecycleScope Scope);
// Null Board means begin Board traversal in Organization; null Card means begin
// that Board. Null Organization means start traversal, never a resumable cursor.
public sealed record GlobalSearchPosition(Guid OrganizationId, Guid? BoardId, Guid? CardId);
public interface IGlobalSearchCursorCodec
{
    string Encode(GlobalSearchBinding binding, GlobalSearchPosition position);
    bool TryDecode(GlobalSearchBinding binding, string token, out GlobalSearchPosition? position);
}
