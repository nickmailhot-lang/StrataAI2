using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private static void MapAttachmentEndpoints(WebApplication app)
    {
        app.MapGet("/cards/{cardId:guid}/attachments/archive", async (Guid cardId, string? after, HttpContext context, AttachmentLifecycleService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "private, no-store";
            var result = await service.ListArchivedAsync(cardId, actor.Value, after, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/cards/{cardId:guid}/attachments/{attachmentId:guid}/archive", async (Guid cardId, Guid attachmentId,
            AttachmentLifecycleInput input, HttpContext context, AttachmentLifecycleService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ArchiveAsync(cardId, attachmentId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/cards/{cardId:guid}/attachments/{attachmentId:guid}/restore", async (Guid cardId, Guid attachmentId,
            AttachmentLifecycleInput input, HttpContext context, AttachmentLifecycleService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.RestoreAsync(cardId, attachmentId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        async Task<IResult> DeleteAttachment(Guid cardId, Guid attachmentId, bool? confirmed, long cardVersion, long version,
            HttpContext context, AttachmentLifecycleService service, CancellationToken ct)
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.DeleteAsync(cardId, attachmentId, actor.Value, new(confirmed == true, cardVersion, version), context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }
        app.MapDelete("/cards/{cardId:guid}/attachments/{attachmentId:guid}", DeleteAttachment)
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        // Canonical deletion is scoped by the current Card query parameter.
        app.MapDelete("/attachments/{attachmentId:guid}", DeleteAttachment)
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/cards/{cardId:guid}/attachments", async (Guid cardId, string? after, HttpContext context, AttachmentService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.ListAsync(cardId, actor.Value, after, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/cards/{cardId:guid}/attachments/url", async (Guid cardId, CreateUrlAttachmentInput input, HttpContext context, AttachmentService service, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await service.CreateUrlAsync(cardId, actor.Value, input, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        if (!app.Services.GetRequiredService<AttachmentUploadAvailability>().Enabled) return;
        MapArchivedAttachmentDelivery(app);
        app.MapGet("/cards/{cardId:guid}/attachments/{attachmentId:guid}/preview-options", async (Guid cardId, Guid attachmentId,
            HttpContext context, AttachmentDownloadAdmissionService admission, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "private, no-store";
            var result = await admission.AdmitPreviewAsync(cardId, attachmentId, actor.Value, ct);
            return result.Succeeded && result.Value is { Source: { } value }
                ? Results.Ok(new AttachmentDownloadOptions(value.Card.OrganizationId, value.Card.BoardId, value.Card.Id,
                    value.Card.Version, value.File.Metadata.Id, value.File.Metadata.Version, actor.Value)) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        async Task<IResult> Preview(Guid cardId, Guid attachmentId, Guid? actorId, long? attachmentVersion, HttpContext context,
            AttachmentPreviewReadService service, AttachmentDownloadAdmissionService admission, CancellationToken ct)
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            if (actorId is { } expectedActor && expectedActor != actor.Value) return ErrorFor("card_not_found");
            var result = await service.PrepareAsync(cardId, attachmentId, actor.Value, ct, attachmentVersion);
            return result.Succeeded && result.Value is not null
                ? new AttachmentPreviewResult(result.Value, admission, actor.Value) : ErrorFor(result.ErrorCode);
        }
        app.MapGet("/cards/{cardId:guid}/attachments/{attachmentId:guid}/preview", Preview)
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/attachments/{attachmentId:guid}/preview", Preview)
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/cards/{cardId:guid}/attachments/{attachmentId:guid}/download-options", async (Guid cardId, Guid attachmentId,
            HttpContext context, AttachmentDownloadAdmissionService admission, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "private, no-store";
            var result = await admission.AdmitAsync(cardId, attachmentId, actor.Value, ct);
            return result.Succeeded && result.Value is { } value
                ? Results.Ok(new AttachmentDownloadOptions(value.Card.OrganizationId, value.Card.BoardId, value.Card.Id,
                    value.Card.Version, value.File.Metadata.Id, value.File.Metadata.Version, actor.Value)) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        async Task<IResult> Download(Guid cardId, Guid attachmentId, Guid? actorId, long? attachmentVersion, HttpContext context, AttachmentDownloadService service,
            AttachmentDownloadAdmissionService admission, CancellationToken ct)
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            if (actorId is { } expectedActor && expectedActor != actor.Value) return ErrorFor("card_not_found");
            var result = await service.PrepareAsync(cardId, attachmentId, actor.Value, ct, attachmentVersion);
            return result.Succeeded && result.Value is not null
                ? new AttachmentDownloadResult(result.Value, admission, actor.Value) : ErrorFor(result.ErrorCode);
        }
        app.MapGet("/cards/{cardId:guid}/attachments/{attachmentId:guid}/download", Download)
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        // The canonical attachment route requires its current Card context;
        // no global metadata lookup or caller-selected object key is exposed.
        app.MapGet("/attachments/{attachmentId:guid}/download", Download)
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/cards/{cardId:guid}/attachment-upload-options", async (Guid cardId, HttpContext context,
            AttachmentUploadAdmissionService admission, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            var result = await admission.GetOptionsAsync(cardId, actor.Value, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapPost("/cards/{cardId:guid}/attachments", async (Guid cardId, HttpContext context,
            AttachmentUploadAdmissionService admission, AttachmentFileUploadService service,
            AttachmentUploadPolicy policy, IWorkCommandContext commands, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            // Do not inspect header claims, disclose policy or consume any body
            // until current session and protected scope have been admitted.
            var admitted = await admission.CheckRequestAsync(cardId, actor.Value, ct);
            if (!admitted.Succeeded) return ErrorFor(admitted.ErrorCode);
            if (commands.IdempotencyKey is not { } key) return ErrorFor("invalid_idempotency_key");
            var input = AttachmentUploadTransport.Read(context.Request);
            if (input is null) return ErrorFor("invalid_attachment_upload");
            if (input.SizeBytes > policy.MaximumBytes) return ErrorFor("attachment_too_large");
            var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = policy.MaximumBytes;
            var result = await service.UploadAsync(cardId, actor.Value, key, input, context.Request.Body, context.TraceIdentifier, ct);
            return result.Succeeded && result.Value is not null ? Results.Ok(result.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }

    private static void MapArchivedAttachmentDelivery(WebApplication app)
    {
        async Task<IResult> Options(Guid cardId, Guid attachmentId, HttpContext context,
            AttachmentDownloadAdmissionService admission, CancellationToken ct, bool preview)
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "private, no-store";
            AttachmentDownloadAdmission? value;
            string? error;
            if (preview)
            {
                var result = await admission.AdmitPreviewAsync(cardId, attachmentId, actor.Value, ct, archiveReview: true);
                value = result.Value?.Source; error = result.ErrorCode;
            }
            else
            {
                var result = await admission.AdmitAsync(cardId, attachmentId, actor.Value, ct, archiveReview: true);
                value = result.Value; error = result.ErrorCode;
            }
            return value is not null ? Results.Ok(new AttachmentDownloadOptions(value.Card.OrganizationId,
                value.Card.BoardId, value.Card.Id, value.Card.Version, value.File.Metadata.Id,
                value.File.Metadata.Version, actor.Value)) : ErrorFor(error);
        }
        app.MapGet("/cards/{cardId:guid}/attachments/archive/{attachmentId:guid}/download-options",
            (Guid cardId, Guid attachmentId, HttpContext context, AttachmentDownloadAdmissionService admission, CancellationToken ct)
                => Options(cardId, attachmentId, context, admission, ct, false))
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/cards/{cardId:guid}/attachments/archive/{attachmentId:guid}/preview-options",
            (Guid cardId, Guid attachmentId, HttpContext context, AttachmentDownloadAdmissionService admission, CancellationToken ct)
                => Options(cardId, attachmentId, context, admission, ct, true))
            .RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/cards/{cardId:guid}/attachments/archive/{attachmentId:guid}/download", async (Guid cardId, Guid attachmentId,
            Guid? actorId, long? attachmentVersion, HttpContext context, AttachmentDownloadService service,
            AttachmentDownloadAdmissionService admission, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            if (actorId is { } expected && expected != actor.Value) return ErrorFor("card_not_found");
            var result = await service.PrepareAsync(cardId, attachmentId, actor.Value, ct, attachmentVersion, archiveReview: true);
            return result.Succeeded && result.Value is not null
                ? new AttachmentDownloadResult(result.Value, admission, actor.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
        app.MapGet("/cards/{cardId:guid}/attachments/archive/{attachmentId:guid}/preview", async (Guid cardId, Guid attachmentId,
            Guid? actorId, long? attachmentVersion, HttpContext context, AttachmentPreviewReadService service,
            AttachmentDownloadAdmissionService admission, CancellationToken ct) =>
        {
            var actor = GetUserId(context); if (actor is null) return Results.Unauthorized();
            if (actorId is { } expected && expected != actor.Value) return ErrorFor("card_not_found");
            var result = await service.PrepareAsync(cardId, attachmentId, actor.Value, ct, attachmentVersion, archiveReview: true);
            return result.Succeeded && result.Value is not null
                ? new AttachmentPreviewResult(result.Value, admission, actor.Value) : ErrorFor(result.ErrorCode);
        }).RequireAuthorization().AddEndpointFilter<BoardSharingResultFilter>();
    }
}
