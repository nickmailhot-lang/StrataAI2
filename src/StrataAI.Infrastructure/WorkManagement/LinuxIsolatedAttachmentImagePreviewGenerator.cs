using System.Diagnostics;
using System.Security.Cryptography;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Supplied by the Worker executable, never an HTTP/provider configuration.
public sealed record AttachmentPreviewWorkerProcess
{
    public AttachmentPreviewWorkerProcess(string dotnetHost, string assembly)
    {
        if (!Path.IsPathFullyQualified(dotnetHost) || !Path.IsPathFullyQualified(assembly)
            || Path.GetFileName(dotnetHost) is not ("dotnet" or "dotnet.exe") || Path.GetFileName(assembly) != "StrataAI.Worker.dll")
            throw new ArgumentException("The fixed Worker executable is required.");
        DotnetHost = dotnetHost; Assembly = assembly;
    }
    public string DotnetHost { get; }
    public string Assembly { get; }
}

// One child at a time per singleton Worker instance. The child receives only
// bounded pipe data, never credentials, provider paths or authorization claims.
public sealed class LinuxIsolatedAttachmentImagePreviewGenerator(AttachmentPreviewWorkerProcess executable)
    : IAttachmentImagePreviewGenerator
{
    private readonly SemaphoreSlim _slots = new(1, 1);
    public async Task<AttachmentPreviewImage> GenerateAsync(AttachmentScanRequest request, string verifiedMimeType,
        Stream verifiedSource, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(verifiedSource); ct.ThrowIfCancellationRequested();
        if (verifiedMimeType is not ("image/png" or "image/jpeg" or "image/webp"))
            throw new AttachmentImagePreviewException("preview_type_unsupported");
        if (!LinuxAttachmentPreviewContainment.Supported || !File.Exists(executable.DotnetHost)
            || executable.DotnetHost != "/usr/share/dotnet/dotnet" || executable.Assembly != "/app/StrataAI.Worker.dll"
            || !File.Exists(executable.Assembly) || !File.Exists("/usr/bin/setpriv") || !File.Exists("/app/strata-preview-launcher")) throw Unavailable(AttachmentPreviewFailureStage.Invocation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var acquired = false; Process? child = null; Task<AttachmentPreviewImage>? result = null; Task[] pending = []; var transferred = false; string? scratch = null;
        Task<AttachmentPreviewFailureStage>? diagnostics = null;
        try
        {
            await _slots.WaitAsync(deadline.Token); acquired = true;
            if (!OperatingSystem.IsLinux()) throw Unavailable();
            scratch = $"/tmp/strata-preview-sandbox-{Guid.NewGuid():N}";
            Directory.CreateDirectory(scratch, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (LinuxAttachmentPreviewContainment.IsRoot) LinuxAttachmentPreviewContainment.AssignScratchOwner(scratch);
            var start = new ProcessStartInfo("/usr/bin/setpriv")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = "/tmp"
            };
            start.ArgumentList.Add("--no-new-privs"); start.ArgumentList.Add("--inh-caps=-all"); start.ArgumentList.Add("--ambient-caps=-all");
            if (LinuxAttachmentPreviewContainment.IsRoot)
            {
                start.ArgumentList.Add("--reuid=1654"); start.ArgumentList.Add("--regid=1654");
                start.ArgumentList.Add("--clear-groups"); start.ArgumentList.Add("--bounding-set=-all");
            }
            start.ArgumentList.Add("--"); start.ArgumentList.Add("/app/strata-preview-launcher"); start.ArgumentList.Add(scratch);
            start.Environment.Clear();
            start.Environment["DOTNET_EnableDiagnostics"] = "0";
            start.Environment["DOTNET_GCHeapHardLimit"] = "4000000"; // Hex: 64 MiB managed heap.
            start.Environment["DOTNET_GCRegionRange"] = "10000000"; // Hex: 256 MiB virtual GC region range.
            start.Environment["DOTNET_gcServer"] = "0";
            start.Environment["DOTNET_PROCESSOR_COUNT"] = "1";
            child = Process.Start(start) ?? throw Unavailable();
            result = AttachmentPreviewProcessProtocol.ReadResultAsync(child.StandardOutput.BaseStream, deadline.Token);
            var writer = WriteAndCloseAsync(child.StandardInput.BaseStream, request, verifiedMimeType, verifiedSource, deadline.Token);
            diagnostics = DrainDiagnosticsAsync(child.StandardError.BaseStream, deadline.Token);
            pending = [result, writer, diagnostics, child.WaitForExitAsync(deadline.Token)];
            await Task.WhenAll(pending).WaitAsync(deadline.Token);
            if (child.ExitCode != 0) throw Unavailable();
            transferred = true; return await result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AttachmentImagePreviewException) { throw; }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var stage = child is null ? AttachmentPreviewFailureStage.Invocation : AttachmentPreviewFailureStage.RuntimeLaunch;
            if (child?.HasExited == true) stage = child.ExitCode switch
            { 65 => AttachmentPreviewFailureStage.Invocation, 66 => AttachmentPreviewFailureStage.ResourceBounds,
                67 or 68 => AttachmentPreviewFailureStage.FileSystemRules, 69 => AttachmentPreviewFailureStage.RuntimeExec, _ => stage };
            if (diagnostics?.IsCompletedSuccessfully == true && diagnostics.Result != AttachmentPreviewFailureStage.None)
                stage = diagnostics.Result;
            throw Unavailable(stage);
        }
        finally
        {
            await deadline.CancelAsync();
            var reaped = child is null;
            if (child is not null)
            {
                try
                {
                    if (!child.HasExited) child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    reaped = true;
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException) { }
                try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
                child.Dispose();
            }
            if (!transferred && result?.IsCompletedSuccessfully == true) result.Result.Dispose();
            if (scratch is not null && reaped)
                try { Directory.Delete(scratch, recursive: false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            // An unreaped process must never admit a parallel decoder. A
            // deployment whose kernel cannot reap it requires Worker recovery.
            if (acquired && reaped) _slots.Release();
        }
    }

    private static async Task WriteAndCloseAsync(Stream pipe, AttachmentScanRequest request, string mime, Stream source, CancellationToken ct)
    {
        try { await AttachmentPreviewProcessProtocol.WriteSourceAsync(pipe, request, mime, source, ct); }
        finally { await pipe.DisposeAsync(); }
    }
    private static async Task<AttachmentPreviewFailureStage> DrainDiagnosticsAsync(Stream pipe, CancellationToken ct)
    {
        var buffer = new byte[4097]; var count = 0;
        try
        {
            while (true)
            {
                var read = await pipe.ReadAsync(buffer.AsMemory(count), ct);
                if (read == 0)
                {
                    // Recognize only fixed runtime failure classes. Never
                    // convert arbitrary diagnostics into a string or log them.
                    var bytes = buffer.AsSpan(0, count);
                    if (bytes.IndexOf("GC heap initialization failed"u8) >= 0 || bytes.IndexOf("HRESULT: 0x8007000E"u8) >= 0)
                        return AttachmentPreviewFailureStage.RuntimeMemory;
                    if (bytes.IndexOf("Couldn't find a valid ICU package"u8) >= 0 || bytes.IndexOf("No usable version of libssl"u8) >= 0)
                        return AttachmentPreviewFailureStage.RuntimeLibrary;
                    return AttachmentPreviewFailureStage.None;
                }
                count += read; if (count > 4096) throw Unavailable();
            }
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private static AttachmentImagePreviewException Unavailable(AttachmentPreviewFailureStage stage = AttachmentPreviewFailureStage.None) => new("preview_decoder_unavailable", stage);
}
