using System.Security.Claims;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    public static void MapWorkManagementEndpoints(this WebApplication app)
    {
        MapLabelEndpoints(app);
        MapBoardCardFilterEndpoints(app);
        MapGlobalSearchEndpoints(app);
        MapAssignableBoardMembersEndpoints(app);
        MapCardMemberEndpoints(app);
        MapNotificationEndpoints(app);
        MapWatchEndpoints(app);
        MapCardDateEndpoints(app);
        MapCardReminderEndpoints(app);
        MapChecklistEndpoints(app);
        MapCardCommentEndpoints(app);
        MapActivityFeedEndpoints(app);
        MapAttachmentEndpoints(app);
        app.MapGet("/boards/{boardId:guid}/cards/{cardId:guid}/archived-details",
            async (Guid boardId, Guid cardId, HttpContext context, ArchivedCardDetailService service, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
                var result = await service.ReadAsync(boardId, cardId, actor.Value, ct);
                return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
            }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet(
            "/boards/{boardId:guid}",
            async (
                Guid boardId,
                HttpContext context,
                IWorkManagementService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetBoardAsync(
                    boardId,
                    GetUserId(context),
                    cancellationToken);

                return result.Succeeded && result.Value is not null
                    ? Results.Ok(result.Value)
                    : ErrorFor(result.ErrorCode);
            }).AddEndpointFilter<BoardSharingResultFilter>();

        app.MapGet("/boards/{boardId:guid}/archived-lists", async (Guid boardId, string? after,
            HttpContext context, IWorkManagementService service, CancellationToken cancellationToken) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            Guid? cursor = after is null ? null : Guid.TryParse(after, out var parsed) ? parsed : Guid.Empty;
            var result = await service.ListArchivedListsAsync(boardId, actor.Value, cursor, cancellationToken);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapGet("/boards/{boardId:guid}/archived-cards", async (Guid boardId, string? after,
            HttpContext context, IWorkManagementService service, CancellationToken cancellationToken) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            Guid? cursor = after is null ? null : Guid.TryParse(after, out var parsed) ? parsed : Guid.Empty;
            var result = await service.ListArchivedCardsAsync(boardId, actor.Value, cursor, cancellationToken);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPost(
                "/boards",
                async (
                    CreateBoardRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    if (!TryParseVisibility(
                            request.Visibility,
                            out var visibility))
                    {
                        return Problem(
                            StatusCodes.Status400BadRequest,
                            "invalid_visibility",
                            "Board visibility must be PRIVATE, ORGANIZATION, or PUBLIC.");
                    }

                    var result = await service.CreateBoardAsync(
                        request.OrganizationId,
                        userId.Value,
                        request.Name,
                        request.Description,
                        visibility,
                        request.BackgroundType,
                        request.BackgroundValue,
                        context.TraceIdentifier,
                        cancellationToken);

                    return result.Succeeded && result.Value is not null
                        ? Results.Created(
                            $"/boards/{result.Value.Id}",
                            result.Value)
                        : ErrorFor(result.ErrorCode);
                })
            .RequireAuthorization();

        app.MapPatch(
                "/boards/{boardId:guid}",
                async (
                    Guid boardId,
                    UpdateBoardRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.UpdateBoardAsync(
                        boardId,
                        userId.Value,
                        request.Name,
                        request.Description,
                        request.BackgroundType,
                        request.BackgroundValue,
                        request.Version,
                        context.TraceIdentifier,
                        cancellationToken);

                    return ToMutationResult(result);
                })
            .RequireAuthorization();

        app.MapPatch(
                "/boards/{boardId:guid}/visibility",
                async (
                    Guid boardId,
                    SetBoardVisibilityRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    if (!TryParseVisibility(
                            request.Visibility,
                            out var visibility))
                    {
                        return Problem(
                            StatusCodes.Status400BadRequest,
                            "invalid_visibility",
                            "Board visibility must be PRIVATE, ORGANIZATION, or PUBLIC.");
                    }

                    var result = await service.SetBoardVisibilityAsync(
                        boardId,
                        userId.Value,
                        visibility,
                        request.Version,
                        context.TraceIdentifier,
                        cancellationToken);

                    return ToMutationResult(result);
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPost(
                "/boards/{boardId:guid}/archive",
                async (
                    Guid boardId,
                    VersionRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.ArchiveBoardAsync(
                            boardId,
                            userId.Value,
                            request.Version,
                            context.TraceIdentifier,
                            cancellationToken));
                })
            .RequireAuthorization();

        app.MapPost(
                "/boards/{boardId:guid}/restore",
                async (
                    Guid boardId,
                    VersionRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.RestoreBoardAsync(
                            boardId,
                            userId.Value,
                            request.Version,
                            context.TraceIdentifier,
                            cancellationToken));
                })
            .RequireAuthorization();

        app.MapDelete(
                "/boards/{boardId:guid}",
                async (
                    Guid boardId,
                    long version,
                    bool? confirmed,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.DeleteBoardAsync(
                            boardId,
                            userId.Value,
                            version,
                            context.TraceIdentifier,
                            cancellationToken, confirmed is true));
                })
            .RequireAuthorization();

        app.MapPut(
                "/boards/{boardId:guid}/star",
                async (
                    Guid boardId,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.SetStarAsync(
                        boardId,
                        userId.Value,
                        true,
                        cancellationToken);

                    return result.Succeeded
                        ? Results.NoContent()
                        : ErrorFor(result.ErrorCode);
                })
            .RequireAuthorization();

        app.MapDelete(
                "/boards/{boardId:guid}/star",
                async (
                    Guid boardId,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.SetStarAsync(
                        boardId,
                        userId.Value,
                        false,
                        cancellationToken);

                    return result.Succeeded
                        ? Results.NoContent()
                        : ErrorFor(result.ErrorCode);
                })
            .RequireAuthorization();

        app.MapGet(
                "/boards/{boardId:guid}/members",
                async (
                    Guid boardId,
                    string? after,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    Guid? cursor = null;
                    if (after is not null)
                    {
                        if (!Guid.TryParse(after, out var parsed) || parsed == Guid.Empty)
                            return Problem(400, "invalid_board_member_cursor", "The member cursor must be a nonempty UUID.");
                        cursor = parsed;
                    }
                    var result = await service.ListBoardMembersAsync(
                        boardId,
                        userId.Value,
                        cancellationToken, cursor);

                    if (!result.Succeeded || result.Value is null) return ErrorFor(result.ErrorCode);
                    var items = result.Value.Take(50).ToArray();
                    if (result.Value.Count > 50)
                        context.Response.Headers["X-StrataAI-Next-Cursor"] = items[^1].UserId.ToString();
                    return Results.Ok(items);
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPatch(
                "/boards/{boardId:guid}/members/{targetUserId:guid}",
                async (
                    Guid boardId,
                    Guid targetUserId,
                    SetBoardMemberRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    if (!TryParseBoardRole(request.Role, out var role))
                    {
                        return Problem(
                            StatusCodes.Status400BadRequest,
                            "invalid_board_role",
                            "Board role must be ADMIN or MEMBER.");
                    }

                    if (!TryMemberVersion(context, out var memberVersion))
                        return Problem(400, "invalid_member_version", "The membership version must be a positive integer.");
                    var result = await service.SetBoardMemberAsync(
                        boardId,
                        userId.Value,
                        targetUserId,
                        role,
                        context.TraceIdentifier,
                        cancellationToken, memberVersion);

                    return result.Succeeded && result.Value is not null
                        ? Results.Ok(result.Value)
                        : ErrorFor(result.ErrorCode);
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapDelete(
                "/boards/{boardId:guid}/members/{targetUserId:guid}",
                async (
                    Guid boardId,
                    Guid targetUserId,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    if (!TryMemberVersion(context, out var memberVersion))
                        return Problem(400, "invalid_member_version", "The membership version must be a positive integer.");
                    var result = await service.RemoveBoardMemberAsync(
                        boardId,
                        userId.Value,
                        targetUserId,
                        context.TraceIdentifier,
                        cancellationToken, memberVersion);

                    return result.Succeeded
                        ? Results.NoContent()
                        : ErrorFor(result.ErrorCode);
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPost(
                "/boards/{boardId:guid}/lists",
                async (
                    Guid boardId,
                    CreateListRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.CreateListAsync(
                        boardId,
                        userId.Value,
                        request.Name,
                        request.Rank,
                        context.TraceIdentifier,
                        cancellationToken);

                    return result.Succeeded && result.Value is not null
                        ? Results.Created(
                            $"/lists/{result.Value.Id}",
                            result.Value)
                        : ErrorFor(result.ErrorCode);
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPatch(
                "/lists/{listId:guid}",
                async (
                    Guid listId,
                    UpdateListRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.UpdateListAsync(
                            listId,
                            userId.Value,
                            request.Name,
                            request.Rank,
                            request.Version,
                            context.TraceIdentifier,
                            cancellationToken, request.BeforeListId, request.MoveToEnd));
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        MapListLifecycle(app, "archive", WorkItemLifecycleState.Archived);
        app.MapPost("/lists/{listId:guid}/copy", async (Guid listId, CopyListRequest request,
            HttpContext context, IWorkManagementService service, CancellationToken cancellationToken) =>
        {
            var actor = GetUserId(context);
            if (actor is null) return Results.Unauthorized();
            var result = await service.CopyListAsync(listId, request.DestinationBoardId, actor.Value,
                request.Name, request.Version, context.TraceIdentifier, cancellationToken);
            return result.Succeeded && result.Value is not null
                ? Results.Created($"/lists/{result.Value.Id}", result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        MapListLifecycle(app, "restore", WorkItemLifecycleState.Active);

        app.MapDelete(
                "/lists/{listId:guid}",
                async (
                    Guid listId,
                    long version,
                    bool? confirmed,
                    long? containedCardCount,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.SetListLifecycleAsync(
                            listId,
                            userId.Value,
                            WorkItemLifecycleState.Deleted,
                            version,
                            context.TraceIdentifier,
                            cancellationToken, confirmed is true, containedCardCount));
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPost(
                "/lists/{listId:guid}/cards",
                async (
                    Guid listId,
                    CreateCardRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var result = await service.CreateCardAsync(
                        listId,
                        userId.Value,
                        request.Title,
                        request.Description,
                        request.Rank,
                        context.TraceIdentifier,
                        cancellationToken);

                    return result.Succeeded && result.Value is not null
                        ? Results.Created(
                            $"/cards/{result.Value.Id}",
                            result.Value)
                        : ErrorFor(result.ErrorCode);
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPatch(
                "/cards/{cardId:guid}",
                async (
                    Guid cardId,
                    UpdateCardRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.UpdateCardAsync(
                            cardId,
                            userId.Value,
                            request.Title,
                            request.Description,
                            request.Version,
                            context.TraceIdentifier,
                            cancellationToken));
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPost("/cards/{cardId:guid}/copy", async (Guid cardId, CopyCardRequest request,
            HttpContext context, IWorkManagementService service, CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(context);
            if (userId is null) return Results.Unauthorized();
            return ToMutationResult(await service.CopyCardAsync(cardId, request.SourceBoardId, request.DestinationListId,
                userId.Value, request.Title, request.ExpectedVersion, context.TraceIdentifier, cancellationToken));
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        app.MapPost(
                "/cards/{cardId:guid}/move",
                async (
                    Guid cardId,
                    MoveCardRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.MoveCardAsync(
                            cardId,
                            userId.Value,
                            request.DestinationListId,
                            request.Rank,
                            request.ExpectedVersion,
                            context.TraceIdentifier,
                            cancellationToken, request.BeforeCardId, request.SourceBoardId));
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();

        MapCardLifecycle(app, "archive", WorkItemLifecycleState.Archived);
        MapCardLifecycle(app, "restore", WorkItemLifecycleState.Active);

        app.MapDelete(
                "/cards/{cardId:guid}",
                async (
                    Guid cardId,
                    long version,
                    bool? confirmed,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.SetCardLifecycleAsync(
                            cardId,
                            userId.Value,
                            WorkItemLifecycleState.Deleted,
                            version,
                            context.TraceIdentifier,
                            cancellationToken, confirmed is true));
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }

    private static void MapListLifecycle(
        WebApplication app,
        string action,
        WorkItemLifecycleState state)
    {
        app.MapPost(
                $"/lists/{{listId:guid}}/{action}",
                async (
                    Guid listId,
                    VersionRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.SetListLifecycleAsync(
                            listId,
                            userId.Value,
                            state,
                            request.Version,
                            context.TraceIdentifier,
                            cancellationToken));
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }

    private static void MapCardLifecycle(
        WebApplication app,
        string action,
        WorkItemLifecycleState state)
    {
        app.MapPost(
                $"/cards/{{cardId:guid}}/{action}",
                async (
                    Guid cardId,
                    VersionRequest request,
                    HttpContext context,
                    IWorkManagementService service,
                    CancellationToken cancellationToken) =>
                {
                    var userId = GetUserId(context);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    return ToMutationResult(
                        await service.SetCardLifecycleAsync(
                            cardId,
                            userId.Value,
                            state,
                            request.Version,
                            context.TraceIdentifier,
                            cancellationToken));
                })
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }

    private static bool TryMemberVersion(HttpContext context, out long? version)
    {
        version = null;
        if (!context.Request.Headers.TryGetValue("If-Match", out var values)) return true;
        if (values.Count != 1) return false;
        var value = values[0]?.Trim() ?? "";
        if (value.Length > 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) || parsed <= 0) return false;
        version = parsed;
        return true;
    }

    private static IResult ToMutationResult<T>(
        WorkOperation<T> result) =>
        result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ErrorFor(result.ErrorCode);

    private static Guid? GetUserId(HttpContext context)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static bool TryParseVisibility(
        string? value,
        out BoardVisibility visibility)
    {
        switch ((value ?? "PRIVATE").Trim().ToUpperInvariant())
        {
            case "PRIVATE":
                visibility = BoardVisibility.Private;
                return true;
            case "ORGANIZATION":
                visibility = BoardVisibility.Organization;
                return true;
            case "PUBLIC":
                visibility = BoardVisibility.Public;
                return true;
            default:
                visibility = default;
                return false;
        }
    }

    private static bool TryParseBoardRole(
        string value,
        out BoardRole role)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case "ADMIN":
                role = BoardRole.Admin;
                return true;
            case "MEMBER":
                role = BoardRole.Member;
                return true;
            default:
                role = default;
                return false;
        }
    }

    private static IResult ErrorFor(string? errorCode) =>
        errorCode switch
        {
            "activity_not_found" => Problem(404, errorCode, "This activity is unavailable."),
            "invalid_activity_cursor" => Problem(400, errorCode, "Reload activity or use this view's current continuation."),
            "activity_unavailable" => Problem(503, errorCode, "Activity could not be confirmed. Try again."),
            "notification_not_found" => Problem(404, errorCode, "The notifications are unavailable."),
            "watch_not_found" => Problem(404, errorCode, "This watch or entity is unavailable."),
            "card_reminder_not_found" => Problem(404, errorCode, "This Card or personal reminder is unavailable."),
            "invalid_card_reminder_version" => Problem(400, errorCode, "Use the current Card and personal reminder revisions."),
            "invalid_card_reminder_interval" => Problem(400, errorCode, "Choose an available future reminder interval."),
            "invalid_card_dates" => Problem(400, errorCode, "Use UTC instants or calendar dates with IANA timezone context and valid date order."),
            "invalid_attachment_cursor" => Problem(400, errorCode, "Use the continuation cursor for this Card."),
            "attachment_not_found" => Problem(404, errorCode, "The attachment is unavailable."),
            "invalid_attachment_version" => Problem(400, errorCode, "Use the current Card revision."),
            "invalid_card_cover" => Problem(400, errorCode, "Use the current Card and image attachment revisions, or null to remove its cover."),
            "cover_public_confirmation_required" => Problem(400, errorCode, "Confirm that this selected image derivative is visible on the public Board."),
            "invalid_attachment_url" => Problem(400, errorCode, "Use a bounded title and an HTTP(S) link without embedded credentials."),
            "invalid_attachment_upload" => Problem(400, errorCode, "Use a file name, original size/digest/revision and an octet-stream body."),
            "invalid_idempotency_key" => Problem(400, errorCode, "A nonempty UUID retry key is required."),
            "attachment_too_large" => Problem(413, errorCode, "The file exceeds the upload size limit."),
            "attachment_type_not_allowed" => Problem(400, errorCode, "The actual file type is unavailable for upload."),
            "attachment_integrity_invalid" => Problem(400, errorCode, "The complete file did not match the original upload."),
            "attachment_source_unavailable" => Problem(400, errorCode, "The file source is unavailable."),
            "attachment_upload_in_progress" => Problem(409, errorCode, "The original upload is still being processed. Retry the same request later."),
            "attachment_upload_unavailable" => Problem(409, errorCode, "Unable to resume this upload. Check the current Card before starting another change."),
            "invalid_checklist_cursor" => Problem(400, errorCode, "Use the continuation cursor for this Card."),
            "invalid_comment_cursor" => Problem(400, errorCode, "Use the continuation cursor for this Card."),
            "invalid_mention_cursor" => Problem(400, errorCode, "Use the continuation cursor for this Card and search."),
            "mention_prefix_invalid" => Problem(400, errorCode, "Use a bounded username prefix containing letters, digits or underscores."),
            "invalid_comment_version" => Problem(400, errorCode, "Use the current Card and comment revisions."),
            "invalid_comment_content" => Problem(400, errorCode, "Comment text must be nonblank, valid Unicode and at most 10000 characters."),
            "invalid_comment_mentions" => Problem(400, errorCode, "Review the comment's username mentions and selected teammates; use at most 20 recipients."),
            "mention_targets_changed" => Problem(409, errorCode, "Mention recipients changed. Review the current comment and teammates."),
            "invalid_mass_mention_confirmation" => Problem(400, errorCode, "Confirm only group mentions present in the comment."),
            "mass_mention_rate_limited" => Problem(429, errorCode, "Group mentions are limited to three deliveries per board in ten minutes. Wait before trying again."),
            "comment_delete_confirmation_required" => Problem(400, errorCode, "Confirm removal of this comment body."),
            "comment_not_found" => Problem(404, errorCode, "The requested comment action is unavailable."),
            "invalid_checklist_version" => Problem(400, errorCode, "Use the current Card revision."),
            "invalid_checklist_title" => Problem(400, errorCode, "Checklist title is required and must fit its limit."),
            "invalid_card_date_version" => Problem(400, errorCode, "Use the current Card revision."),
            "invalid_board_date_policy" => Problem(400, errorCode, "Use a valid IANA timezone or null and the current Board revision."),
            "invalid_watch_version" => Problem(400, errorCode, "Use the current watch revision, or zero for a new subscription."),
            "invalid_notification_cursor" => Problem(400, errorCode, "Use the notification page's current cursor."),
            "invalid_notification_selection" => Problem(400, errorCode, "Select between one and 50 distinct notifications."),
            "invalid_label_name" => Problem(400, errorCode, "A label name must contain at most 160 characters."),
            "invalid_label_color" => Problem(400, errorCode, "Choose a supported label color."),
            "invalid_label_cursor" => Problem(400, errorCode, "The label cursor must be a nonempty UUID."),
            "invalid_board_filter" => Problem(400, errorCode, "Use a keyword of at most 160 characters, up to 25 distinct label IDs, ANY or ALL, and a valid cursor."),
            "invalid_search" => Problem(400, errorCode, "Use text criteria of at most 160 characters, ANY or ALL, active or archived scope, and this search's current cursor."),
            "invalid_board_member_cursor" => Problem(400, errorCode, "The member cursor must be a nonempty UUID."),
            "invalid_card_member_version" => Problem(400, errorCode, "Use the Card's current positive revision."),
            "label_not_found" => Problem(404, errorCode, "The label was not found."),
            "session_unavailable" => Problem(
                StatusCodes.Status401Unauthorized,
                errorCode,
                "Your session is no longer available. Sign in again."),
            "work_storage_unavailable" => Problem(
                StatusCodes.Status503ServiceUnavailable,
                errorCode,
                "Unable to confirm the change. Refresh before retrying."),
            "invalid_board_name" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A valid Board name is required."),
            "invalid_background" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "The Board background is invalid."),
            "invalid_archive_cursor" => Problem(StatusCodes.Status400BadRequest, errorCode, "The archive cursor must be a nonempty UUID."),
            "delete_confirmation_required" => Problem(StatusCodes.Status400BadRequest, errorCode, "Explicit permanent-deletion confirmation is required."),
            "deletion_impact_required" => Problem(StatusCodes.Status400BadRequest, errorCode, "Review the current contained-card impact before deletion."),
            "deletion_impact_changed" => Problem(StatusCodes.Status409Conflict, errorCode, "The contained-card impact changed. Review the current archive before deletion."),
            "invalid_list_name" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A valid List name is required."),
            "invalid_card_title" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "A Card title is required."),
            "invalid_rank" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "The requested rank is invalid."),
            "invalid_move_position" => Problem(
                StatusCodes.Status400BadRequest, errorCode, "The requested position is invalid."),
            "rank_space_exhausted" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "No ordering space remains at this position."),
            "idempotency_key_reused" => Problem(
                StatusCodes.Status409Conflict, errorCode, "This retry key belongs to a different request."),
            "idempotency_key_expired" => Problem(
                StatusCodes.Status409Conflict, errorCode, "This retry key expired. Check the latest state before starting a new change."),
            "version_conflict" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "The item changed elsewhere. Refresh and retry."),
            "invalid_lifecycle_transition" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "The requested lifecycle transition is not valid."),
            "sole_board_admin" => Problem(
                StatusCodes.Status409Conflict,
                errorCode,
                "The last Board administrator cannot be removed or demoted by this actor."),
            "member_not_eligible" => Problem(
                StatusCodes.Status400BadRequest,
                errorCode,
                "The user is not an active member of this Organization."),
            "member_not_found" => Problem(
                StatusCodes.Status404NotFound,
                errorCode,
                "The Board member was not found."),
            "list_not_found" => Problem(
                StatusCodes.Status404NotFound,
                errorCode,
                "The List was not found."),
            "invalid_checklist_item_text" => Problem(
                StatusCodes.Status400BadRequest, errorCode, "Checklist item text must be nonblank and at most 2000 characters."),
            "checklist_item_not_found" => Problem(
                StatusCodes.Status404NotFound, errorCode, "The requested checklist item is unavailable."),
            "checklist_not_found" => Problem(
                StatusCodes.Status404NotFound, errorCode, "The requested checklist is unavailable."),
            "card_not_found" => Problem(
                StatusCodes.Status404NotFound,
                errorCode,
                "The Card was not found."),
            "organization_not_found" => Problem(
                StatusCodes.Status404NotFound,
                errorCode,
                "The Organization was not found."),
            _ => Problem(
                StatusCodes.Status404NotFound,
                "board_not_found",
                "The Board was not found."),
        };

    private static IResult Problem(
        int status,
        string code,
        string title) =>
        Results.Problem(
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
            });
}
