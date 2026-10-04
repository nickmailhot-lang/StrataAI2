using System.Text.Json.Serialization;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed class BoardBackgroundImageAdmission
{
    internal BoardBackgroundImageAdmission(Guid? actor, BoardRecord board, BoardBackgroundImage image, DateTimeOffset at)
    { ActorId = actor; Board = board; Image = image; AdmittedAt = at; ExpiresAt = at.AddMinutes(1); }
    [JsonIgnore] public Guid? ActorId { get; }
    [JsonIgnore] public BoardRecord Board { get; }
    [JsonIgnore] public BoardBackgroundImage Image { get; }
    [JsonIgnore] public DateTimeOffset AdmittedAt { get; }
    [JsonIgnore] public DateTimeOffset ExpiresAt { get; }
}

public sealed class BoardBackgroundImageAdmissionService(IWorkManagementStore work, IOrganizationStore organizations,
    IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions, ICommandActorAuthorization actors, IClock clock)
{
    public async Task<WorkOperation<BoardBackgroundImageAdmission>> AdmitAsync(Guid board, Guid? actor, CancellationToken ct = default)
    {
        var hint = await work.FindBoardAsync(board, ct);
        if (hint is null) return WorkOperation<BoardBackgroundImageAdmission>.Failure("board_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "board_not_found", () => Scope(hint, actor, ct), async () =>
        {
            var current = await work.FindBoardAsync(board, ct);
            if (current is null || current.BackgroundType != "IMAGE" || !Guid.TryParseExact(current.BackgroundValue, "D", out var id))
                return WorkOperation<BoardBackgroundImageAdmission>.Failure("board_not_found");
            var image = await work.FindBoardBackgroundImageAsync(hint.OrganizationId, board, id, ct);
            return Valid(image, current) ? WorkOperation<BoardBackgroundImageAdmission>.Success(new(actor, current, image!, clock.UtcNow))
                : WorkOperation<BoardBackgroundImageAdmission>.Failure("board_not_found");
        }, ct);
    }

    public async Task<WorkOperation<bool>> RevalidateAsync(BoardBackgroundImageAdmission admitted, Guid? actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        bool Timely() => actor == admitted.ActorId && clock.UtcNow >= admitted.AdmittedAt && clock.UtcNow < admitted.ExpiresAt;
        if (!Timely()) return WorkOperation<bool>.Failure("board_not_found");
        return await transactions.ExecuteReadAsync(admitted.Board.OrganizationId, actor, "board_not_found",
            async () => Timely() && await Scope(admitted.Board, actor, ct), async () =>
            {
                var current = await work.FindBoardAsync(admitted.Board.Id, ct);
                var image = await work.FindBoardBackgroundImageAsync(admitted.Board.OrganizationId, admitted.Board.Id, admitted.Image.Id, ct);
                return Timely() && current == admitted.Board && image == admitted.Image && Valid(image, current)
                    ? WorkOperation<bool>.Success(true) : WorkOperation<bool>.Failure("board_not_found");
            }, ct);
    }

    private async Task<bool> Scope(BoardRecord hint, Guid? actor, CancellationToken ct)
    {
        var initial = (await boards.GetSyncScopeAsync(hint.Id, actor, ct)).Value;
        if (initial is null || !initial.Access.CanView || initial.Board.OrganizationId != hint.OrganizationId) return false;
        if (initial.Board.Visibility == BoardVisibility.Public)
        { if (!await work.AcquirePublicBoardBackgroundReadScopeAsync(hint.OrganizationId, hint.Id, ct)) return false; }
        else if (actor is not { } member || !await work.AcquireBoardReadScopeAsync(hint.OrganizationId, member, hint.Id, ct)
            || await organizations.FindMembershipAsync(hint.OrganizationId, member, ct) is not { Active: true }) return false;
        var current = (await boards.GetSyncScopeAsync(hint.Id, actor, ct)).Value;
        return current is { Access.CanView: true, Board.LifecycleState: BoardLifecycleState.Active }
            && current.Board.OrganizationId == hint.OrganizationId && (actor is not null || current.Board.Visibility == BoardVisibility.Public)
            && await organizations.FindOrganizationAsync(hint.OrganizationId, ct) is { Status: OrganizationStatus.Active }
            && (actor is not { } account || await actors.VerifyAsync(account, ct));
    }
    private static bool Valid(BoardBackgroundImage? image, BoardRecord? board) => image is not null && board is not null
        && image.OrganizationId == board.OrganizationId && image.BoardId == board.Id && board.BackgroundType == "IMAGE"
        && board.BackgroundValue == image.Id.ToString("D") && image.Preview.Integrity.Reference.IsPreview
        && image.Preview.Integrity.Reference.OrganizationId == board.OrganizationId
        && image.Preview.Integrity.SizeBytes is >= 45 and <= 8388608 && image.Preview.Width is >= 1 and <= 1024 && image.Preview.Height is >= 1 and <= 1024;
}

public sealed class BoardBackgroundImageContent : IAsyncDisposable
{
    internal BoardBackgroundImageContent(Stream bytes, BoardBackgroundImageAdmission admission) { Bytes = bytes; Admission = admission; }
    [JsonIgnore] public Stream Bytes { get; }
    [JsonIgnore] public BoardBackgroundImageAdmission Admission { get; }
    [JsonIgnore] public long SizeBytes => Admission.Image.Preview.Integrity.SizeBytes;
    public ValueTask DisposeAsync() => Bytes.DisposeAsync();
}
public sealed class BoardBackgroundImageReadService(BoardBackgroundImageAdmissionService admission, IAttachmentDownloadPreparer preparer)
{
    public async Task<WorkOperation<BoardBackgroundImageContent>> PrepareAsync(Guid board, Guid? actor, CancellationToken ct = default, long? boardVersion = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(55));
        Stream? prepared = null;
        try
        {
            var admitted = await admission.AdmitAsync(board, actor, deadline.Token);
            if (!admitted.Succeeded || admitted.Value is null) return WorkOperation<BoardBackgroundImageContent>.Failure(admitted.ErrorCode ?? "board_not_found");
            if (boardVersion is { } revision && revision != admitted.Value.Board.Version) return WorkOperation<BoardBackgroundImageContent>.Failure("board_not_found");
            prepared = await preparer.PrepareAsync(admitted.Value.Image.Preview.Integrity, deadline.Token);
            if (prepared is null) return WorkOperation<BoardBackgroundImageContent>.Failure("board_not_found");
            var current = await admission.RevalidateAsync(admitted.Value, actor, deadline.Token);
            if (!current.Succeeded) return WorkOperation<BoardBackgroundImageContent>.Failure(current.ErrorCode ?? "board_not_found");
            deadline.Token.ThrowIfCancellationRequested(); var result = new BoardBackgroundImageContent(prepared, admitted.Value); prepared = null;
            return WorkOperation<BoardBackgroundImageContent>.Success(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is OperationCanceledException or AttachmentStorageException or IOException or UnauthorizedAccessException)
        { return WorkOperation<BoardBackgroundImageContent>.Failure("work_storage_unavailable"); }
        finally { if (prepared is not null) await prepared.DisposeAsync(); }
    }
}
