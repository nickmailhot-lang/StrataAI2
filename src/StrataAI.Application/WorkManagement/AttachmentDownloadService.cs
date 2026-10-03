using System.Text.Json.Serialization;

namespace StrataAI.Application.WorkManagement;

public interface IAttachmentDownloadPreparer
{
    // Read the complete private object and verify exact size/SHA before
    // returning any bytes for delivery. Returned streams are caller-owned.
    Task<Stream?> PrepareAsync(AttachmentScanRequest request, CancellationToken ct);
}

public sealed class AttachmentDownloadContent : IAsyncDisposable
{
    internal AttachmentDownloadContent(Stream bytes, AttachmentDownloadAdmission admission)
    { Bytes = bytes; Admission = admission; DisplayName = admission.File.Metadata.DisplayName; SizeBytes = admission.File.Metadata.SizeBytes!.Value; }
    [JsonIgnore] public AttachmentDownloadAdmission Admission { get; }
    [JsonIgnore] public Stream Bytes { get; }
    [JsonIgnore] public string DisplayName { get; }
    [JsonIgnore] public long SizeBytes { get; }
    public ValueTask DisposeAsync() => Bytes.DisposeAsync();
}

public sealed class AttachmentDownloadService(AttachmentDownloadAdmissionService admission, IAttachmentDownloadPreparer preparer)
{
    public async Task<WorkOperation<AttachmentDownloadContent>> PrepareAsync(Guid card, Guid attachment, Guid actor,
        CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        Stream? prepared = null;
        try
        {
            var admitted = await admission.AdmitAsync(card, attachment, actor, deadline.Token);
            if (!admitted.Succeeded || admitted.Value is null)
                return WorkOperation<AttachmentDownloadContent>.Failure(admitted.ErrorCode ?? "card_not_found");
            prepared = await preparer.PrepareAsync(admitted.Value.File.Integrity, deadline.Token);
            if (prepared is null) return WorkOperation<AttachmentDownloadContent>.Failure("card_not_found");
            var current = await admission.RevalidateAsync(admitted.Value, actor, deadline.Token);
            if (!current.Succeeded) return WorkOperation<AttachmentDownloadContent>.Failure(current.ErrorCode ?? "card_not_found");
            deadline.Token.ThrowIfCancellationRequested();
            var result = new AttachmentDownloadContent(prepared, admitted.Value);
            prepared = null; // Ownership transfers only after final admission.
            return WorkOperation<AttachmentDownloadContent>.Success(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is OperationCanceledException or AttachmentStorageException or IOException or UnauthorizedAccessException)
        { return WorkOperation<AttachmentDownloadContent>.Failure("work_storage_unavailable"); }
        finally { if (prepared is not null) await prepared.DisposeAsync(); }
    }
}
