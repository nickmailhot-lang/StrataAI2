using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    private readonly bool _publicFixtureVerification;
    private LinuxIsolatedAttachmentImagePreviewGenerator(AttachmentPreviewWorkerProcess executable, bool publicFixtureVerification)
        : this(executable) => _publicFixtureVerification = publicFixtureVerification;

    // Only this factory can select public diagnostic reporting. It constructs
    // its own fixed public bytes and claims; callers cannot supply image data,
    // a provider reference, environment values or another diagnostic input.
    public static async Task<AttachmentPreviewImage> VerifyPublicFixtureAsync(AttachmentPreviewWorkerProcess executable, CancellationToken ct)
    {
        using var source = new MemoryStream(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="), writable: false);
        var request = new AttachmentScanRequest(new(Guid.NewGuid(), Guid.NewGuid()), source.Length,
            Convert.ToHexStringLower(SHA256.HashData(source.ToArray())));
        return await new LinuxIsolatedAttachmentImagePreviewGenerator(executable, publicFixtureVerification: true)
            .GenerateAsync(request, "image/png", source, ct);
    }
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
            AttachmentPreviewProcessProtocol.ConfigureChildEnvironment(start);
            child = Process.Start(start) ?? throw Unavailable();
            result = AttachmentPreviewProcessProtocol.ReadResultAsync(child.StandardOutput.BaseStream, deadline.Token);
            var writer = WriteAndCloseAsync(child.StandardInput.BaseStream, request, verifiedMimeType, verifiedSource, deadline.Token);
            diagnostics = _publicFixtureVerification
                ? DrainPublicFixtureDiagnosticsAsync(child.StandardError.BaseStream, deadline.Token)
                : AttachmentPreviewProcessProtocol.DrainRuntimeFailureAsync(child.StandardError.BaseStream, deadline.Token);
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
                67 or 68 => AttachmentPreviewFailureStage.FileSystemRules, 69 => AttachmentPreviewFailureStage.RuntimeExec,
                127 => AttachmentPreviewFailureStage.RuntimeLoader, 134 => AttachmentPreviewFailureStage.RuntimeAbort,
                137 => AttachmentPreviewFailureStage.RuntimeKilled, 139 => AttachmentPreviewFailureStage.RuntimeFault, _ => stage };
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

    private static async Task<AttachmentPreviewFailureStage> DrainPublicFixtureDiagnosticsAsync(Stream pipe, CancellationToken ct)
    {
        var prefix = new byte[4096]; var discard = new byte[4096]; var count = 0;
        try
        {
            while (true)
            {
                var read = await pipe.ReadAsync(count < prefix.Length ? prefix.AsMemory(count) : discard, ct);
                if (read == 0) break;
                if (count < prefix.Length) count += read;
                else CryptographicOperations.ZeroMemory(discard);
            }
            using var retained = new MemoryStream(prefix, 0, count, writable: false);
            var stage = await AttachmentPreviewProcessProtocol.DrainRuntimeFailureAsync(retained, ct);
            var report = new StringBuilder(count);
            for (var offset = 0; offset < count; offset++)
            {
                if (offset <= count - 9 && prefix.AsSpan(offset, 8).SequenceEqual("SAPRVSTG"u8)) { offset += 8; continue; }
                var value = prefix[offset];
                if (value is >= 32 and <= 126 or 10 or 13) report.Append((char)value);
            }
            if (report.Length > 0)
            {
                Console.Error.WriteLine("Bounded startup report from the fixed public PNG verification child:");
                Console.Error.WriteLine(report.ToString());
            }
            return stage;
        }
        finally { CryptographicOperations.ZeroMemory(prefix); CryptographicOperations.ZeroMemory(discard); }
    }
    private static AttachmentImagePreviewException Unavailable(AttachmentPreviewFailureStage stage = AttachmentPreviewFailureStage.None) => new("preview_decoder_unavailable", stage);
}
