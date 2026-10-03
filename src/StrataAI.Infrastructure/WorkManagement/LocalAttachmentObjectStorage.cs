using System.Security.Cryptography;
using System.Runtime.InteropServices;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Explicit local/test adapter sanctioned by ARCH-07. Not registered as a
// production fallback. Root must be an operator-owned private directory;
// untrusted processes must not be able to rename/replace its directories.
public sealed class LocalAttachmentObjectStorage : IAttachmentObjectStorage
{
    private readonly string _root;
    public LocalAttachmentObjectStorage(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new ArgumentException("An absolute private attachment storage root is required.", nameof(root));
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        RejectLinks(_root); RequirePrivateRoot();
    }
    public async Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream source, long maximumBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Attachment source must be readable.", nameof(source));
        if (maximumBytes is < 1 or > 1073741824) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ct.ThrowIfCancellationRequested();
        var final = ObjectPath(reference); var parent = Path.GetDirectoryName(final)!;
        string? temporary = null;
        try
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(parent);
            else
            {
                Directory.CreateDirectory(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                RequirePrivateRoot();
                Directory.CreateDirectory(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            RejectLinks(parent);
            if (File.Exists(final)) throw new AttachmentStorageException("object_exists");
            temporary = Path.Combine(parent, $"upload-{Guid.NewGuid():N}.tmp");
            long size = 0; string digest;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 65536, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var output = new FileStream(temporary, options))
            {
                var buffer = new byte[65536];
                while (true)
                {
                    // Read at most one byte beyond the limit. No unbounded
                    // buffering, source Length trust or caller MIME trust.
                    var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximumBytes - size + 1)), ct);
                    if (count == 0) break;
                    if (count > maximumBytes - size) throw new AttachmentStorageException("object_too_large");
                    size += count; hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                if (size == 0) throw new AttachmentStorageException("object_empty");
                await output.FlushAsync(ct); output.Flush(flushToDisk: true);
                digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            ct.ThrowIfCancellationRequested(); RejectLinks(parent); RejectLinks(final);
            PublishCompleteObject(temporary, final, ct);
            return new(reference, size, digest);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new AttachmentStorageException(File.Exists(final) ? "object_exists" : "object_storage_unavailable");
        }
        finally
        {
            if (temporary is not null)
            {
                try { RejectLinks(parent); RejectLinks(temporary); File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or AttachmentStorageException) { }
            }
        }
    }
    private static void PublishCompleteObject(string temporary, string final, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) { File.Move(temporary, final, overwrite: false); return; }
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) throw new AttachmentStorageException("object_storage_unavailable");
        // .NET's Unix no-overwrite Move checks existence before rename, which
        // can replace a concurrent winner. POSIX link creates the final name
        // atomically or refuses EEXIST. No copy/rename fallback may expose bytes.
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (LinkPrivateFile(temporary, final) == 0) return;
                var error = Marshal.GetLastPInvokeError();
                if (error == 4) continue; // EINTR: no successful publication.
                throw new AttachmentStorageException(error == 17 ? "object_exists" : "object_storage_unavailable");
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { throw new AttachmentStorageException("object_storage_unavailable"); }
        // finally in WritePrivateAsync removes only this writer's temporary
        // name. On Unix both names refer to the already flushed complete inode.
    }
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkPrivateFile([MarshalAs(UnmanagedType.LPUTF8Str)] string source, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination);
    public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var path = ObjectPath(reference);
        try
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return Task.FromResult<Stream?>(null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { throw new AttachmentStorageException("object_storage_unavailable"); }
    }
    public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var path = ObjectPath(reference);
        try { if (!File.Exists(path)) return Task.FromResult(false); File.Delete(path); return Task.FromResult(true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { throw new AttachmentStorageException("object_storage_unavailable"); }
    }
    private string ObjectPath(AttachmentObjectReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var path = Path.GetFullPath(Path.Combine(_root, "attachments", reference.OrganizationId.ToString("N"), reference.AttachmentId.ToString("N")));
        if (!Path.GetRelativePath(_root, path).StartsWith("attachments" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new AttachmentStorageException("object_scope_invalid");
        RejectLinks(path); RequirePrivateRoot(); return path;
    }
    private void RequirePrivateRoot()
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists(_root)) return;
        try
        {
            if ((File.GetUnixFileMode(_root) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new AttachmentStorageException("object_path_unsafe");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { throw new AttachmentStorageException("object_storage_unavailable"); }
    }
    private static void RejectLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new AttachmentStorageException("object_path_unsafe"); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { throw new AttachmentStorageException("object_storage_unavailable"); }
        }
    }
}
