using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.WorkManagement;

public static partial class WorkManagementEndpoints
{
    private sealed class AttachmentPreviewResult(AttachmentPreviewContent content,
        AttachmentDownloadAdmissionService admission, Guid actor) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            await using var owned = content;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromMinutes(1));
            var ct = deadline.Token; var buffer = new byte[65536];
            try
            {
                var admitted = await admission.RevalidatePreviewAsync(content.Admission, actor, ct);
                if (!admitted.Succeeded)
                {
                    BoardSharingTelemetry.SetError(context, admitted.ErrorCode);
                    await ErrorFor(admitted.ErrorCode).ExecuteAsync(context); return;
                }
                context.Response.ContentType = "image/png";
                context.Response.ContentLength = content.SizeBytes;
                context.Response.Headers.ContentDisposition = "inline; filename=preview.png";
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers.AcceptRanges = "none";
                long remaining = content.SizeBytes; var checkedAt = Stopwatch.GetTimestamp();
                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var read = await content.Bytes.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                    if (read == 0) throw new IOException("Prepared attachment bytes are unavailable.");
                    // Re-admit periodically and before the final bytes. A slow
                    // or revoked request cannot retain an expired snapshot.
                    if (read == remaining || Stopwatch.GetElapsedTime(checkedAt) >= TimeSpan.FromSeconds(1))
                    {
                        var current = await admission.RevalidatePreviewAsync(content.Admission, actor, ct);
                        if (!current.Succeeded) { BoardSharingTelemetry.SetError(context, current.ErrorCode); context.Abort(); return; }
                        checkedAt = Stopwatch.GetTimestamp();
                    }
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct); remaining -= read;
                }
            }
            catch (Exception error) when (error is OperationCanceledException or IOException)
            { BoardSharingTelemetry.SetError(context, "work_storage_unavailable"); context.Abort(); }
            finally { CryptographicOperations.ZeroMemory(buffer); }
        }
    }
}
