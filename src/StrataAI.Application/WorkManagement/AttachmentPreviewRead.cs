using System.Text.Json.Serialization;

namespace StrataAI.Application.WorkManagement;

// Server-only immutable publication data; no provider identifier or digest is
// exposed as an HTTP credential or accepted from clients.
public sealed record AttachmentPublishedPreview(
    [property: JsonIgnore] AttachmentScanRequest Integrity,
    [property: JsonIgnore] int Width,
    [property: JsonIgnore] int Height);

public sealed class AttachmentPreviewAdmission
{
    internal AttachmentPreviewAdmission(AttachmentDownloadAdmission source, AttachmentPublishedPreview preview)
    { Source = source; Preview = preview; }
    [JsonIgnore] public AttachmentDownloadAdmission Source { get; }
    [JsonIgnore] public AttachmentPublishedPreview Preview { get; }
}

public sealed class AttachmentPreviewContent : IAsyncDisposable
{
    internal AttachmentPreviewContent(Stream bytes, AttachmentPreviewAdmission admission)
    { Bytes = bytes; Admission = admission; }
    [JsonIgnore] public Stream Bytes { get; }
    [JsonIgnore] public AttachmentPreviewAdmission Admission { get; }
    [JsonIgnore] public long SizeBytes => Admission.Preview.Integrity.SizeBytes;
    public ValueTask DisposeAsync() => Bytes.DisposeAsync();
}

public sealed class AttachmentPreviewReadService(AttachmentDownloadAdmissionService admission, IAttachmentDownloadPreparer preparer)
{
    public async Task<WorkOperation<AttachmentPreviewContent>> PrepareAsync(Guid card, Guid attachment, Guid actor,
        CancellationToken ct = default, long? attachmentVersion = null, bool archiveReview = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        Stream? prepared = null;
        try
        {
            var admitted = await admission.AdmitPreviewAsync(card, attachment, actor, deadline.Token, archiveReview);
            if (!admitted.Succeeded || admitted.Value is null)
                return WorkOperation<AttachmentPreviewContent>.Failure(admitted.ErrorCode ?? "card_not_found");
            if (attachmentVersion is { } version && version != admitted.Value.Source.File.Metadata.Version)
                return WorkOperation<AttachmentPreviewContent>.Failure("card_not_found");
            prepared = await preparer.PrepareAsync(admitted.Value.Preview.Integrity, deadline.Token);
            if (prepared is null) return WorkOperation<AttachmentPreviewContent>.Failure("card_not_found");
            var current = await admission.RevalidatePreviewAsync(admitted.Value, actor, deadline.Token);
            if (!current.Succeeded) return WorkOperation<AttachmentPreviewContent>.Failure(current.ErrorCode ?? "card_not_found");
            deadline.Token.ThrowIfCancellationRequested();
            var result = new AttachmentPreviewContent(prepared, admitted.Value);
            prepared = null;
            return WorkOperation<AttachmentPreviewContent>.Success(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is OperationCanceledException or AttachmentStorageException or IOException or UnauthorizedAccessException)
        { return WorkOperation<AttachmentPreviewContent>.Failure("work_storage_unavailable"); }
        finally { if (prepared is not null) await prepared.DisposeAsync(); }
    }
}
