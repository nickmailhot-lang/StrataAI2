using System.Runtime.InteropServices;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

// Fail closed outside the verified Linux x64 release target. These restrictions
// apply only inside the dedicated decoder child, never the main Worker/API.
public static class LinuxAttachmentPreviewContainment
{
    public static bool Supported => OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;
    public static bool IsRoot => Supported && GetEffectiveUid() == 0;

    public static void Apply()
    {
        if (!Supported || IsRoot) throw Unavailable(AttachmentPreviewFailureStage.Capabilities);
        var parent = GetParentPid();
        if (Prctl(38, 1, 0, 0, 0) != 0 || Prctl(4, 0, 0, 0, 0) != 0
            || Prctl(1, 9, 0, 0, 0) != 0 || GetParentPid() != parent) throw Unavailable(AttachmentPreviewFailureStage.Capabilities);
        Limit(9, 1073741824); // RLIMIT_AS: native + managed address space, 1 GiB.
        Limit(0, 10); // RLIMIT_CPU: hard CPU deadline, not cooperative cancellation.
        Limit(4, 0); // RLIMIT_CORE: no source-containing core dumps.
        Limit(1, 1073741824); // RLIMIT_FSIZE: staging cannot exceed original limit.
        Limit(7, 256); // RLIMIT_NOFILE: includes CLR/framework assembly handles.
        Limit(6, 64); // RLIMIT_NPROC: threads/processes for the nonroot uid.

        // Reject nonzero capabilities even if a deployment changed uid setup.
        var status = File.ReadAllLines("/proc/self/status");
        foreach (var name in new[] { "CapEff:", "CapPrm:", "CapInh:", "CapAmb:" })
            if (!status.Any(line => line.StartsWith(name, StringComparison.Ordinal)
                && line[name.Length..].Trim() == "0000000000000000")) throw Unavailable(AttachmentPreviewFailureStage.Capabilities);

        // x64 syscall ABI only, including refusal of the x32 ABI. Synchronize
        // the filter onto all existing CLR threads; future threads inherit it.
        var filters = new List<Filter>
        {
            new(0x20, 0, 0, 4), new(0x15, 1, 0, 0xc000003e), new(0x06, 0, 0, 0x80000000),
            new(0x20, 0, 0, 0), new(0x35, 0, 1, 0x40000000), new(0x06, 0, 0, 0x00050001)
        };
        // clone may create CLR threads but cannot fork another process.
        // Return ENOSYS for clone3, whose pointed-to flags BPF cannot inspect;
        // libc's thread creation then uses the inspected legacy clone ABI.
        filters.AddRange(new Filter[] { new(0x15, 0, 4, 56), new(0x20, 0, 0, 16),
            new(0x45, 1, 0, 0x10000), new(0x06, 0, 0, 0x00050001), new(0x06, 0, 0, 0x7fff0000),
            new(0x15, 0, 1, 435), new(0x06, 0, 0, 0x00050026) });
        // Socket operations, process execution/tracing, namespace/mount changes,
        // BPF/userfaultfd and io_uring are not required by CPU raster decoding.
        foreach (uint syscall in new uint[] { 41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,
            57,58,59,101,155,165,166,272,288,299,304,307,308,310,311,321,322,323,425,426,427 })
        {
            filters.Add(new(0x15, 0, 1, syscall)); filters.Add(new(0x06, 0, 0, 0x00050001));
        }
        filters.Add(new(0x06, 0, 0, 0x7fff0000));
        var allocation = Marshal.AllocHGlobal(filters.Count * Marshal.SizeOf<Filter>());
        try
        {
            for (var index = 0; index < filters.Count; index++)
                Marshal.StructureToPtr(filters[index], allocation + index * Marshal.SizeOf<Filter>(), false);
            var program = new FilterProgram { Length = checked((ushort)filters.Count), Instructions = allocation };
            // seccomp(SECCOMP_SET_MODE_FILTER, SECCOMP_FILTER_FLAG_TSYNC, ...).
            if (Seccomp(317, 1, 1, ref program) != 0) throw Unavailable(AttachmentPreviewFailureStage.SyscallFilter);
        }
        finally { Marshal.FreeHGlobal(allocation); }

        // Check the active kernel boundary without generating external traffic,
        // executing a process or allocating/committing a giant image buffer.
        if (ProbeSocket(2, 1, 0) != -1 || Marshal.GetLastPInvokeError() != 1) throw Unavailable(AttachmentPreviewFailureStage.NetworkProbe);
        if (ProbeExec(59, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != -1 || Marshal.GetLastPInvokeError() != 1) throw Unavailable(AttachmentPreviewFailureStage.ExecutionProbe);
        var address = ProbeMap(IntPtr.Zero, 1073745920, 0, 0x22, -1, 0);
        if (address != new IntPtr(-1)) { Unmap(address, 1073745920); throw Unavailable(AttachmentPreviewFailureStage.AddressSpaceProbe); }
        if (Marshal.GetLastPInvokeError() != 12) throw Unavailable(AttachmentPreviewFailureStage.AddressSpaceProbe);
        var outside = Open("/etc/passwd", 0);
        if (outside != -1) { Close(outside); throw Unavailable(AttachmentPreviewFailureStage.FileSystemProbe); }
        if (Marshal.GetLastPInvokeError() != 13) throw Unavailable(AttachmentPreviewFailureStage.FileSystemProbe);
    }

    private static void Limit(int resource, ulong maximum)
    {
        var value = new ResourceLimit { Soft = maximum, Hard = maximum };
        if (SetLimit(resource, ref value) != 0) throw Unavailable(AttachmentPreviewFailureStage.ResourceBounds);
    }
    private static AttachmentImagePreviewException Unavailable(AttachmentPreviewFailureStage stage = AttachmentPreviewFailureStage.None) => new("preview_decoder_unavailable", stage);
    [StructLayout(LayoutKind.Sequential)] private struct ResourceLimit { public ulong Soft; public ulong Hard; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct Filter(ushort code, byte yes, byte no, uint value)
    { public readonly ushort Code = code; public readonly byte Yes = yes; public readonly byte No = no; public readonly uint Value = value; }
    [StructLayout(LayoutKind.Sequential)] private struct FilterProgram { public ushort Length; public IntPtr Instructions; }
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEffectiveUid();
    [DllImport("libc", EntryPoint = "getppid")] private static extern int GetParentPid();
    [DllImport("libc", EntryPoint = "prctl")] private static extern int Prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);
    [DllImport("libc", EntryPoint = "setrlimit")] private static extern int SetLimit(int resource, ref ResourceLimit value);
    [DllImport("libc", EntryPoint = "syscall")] private static extern long Seccomp(long number, int operation, uint flags, ref FilterProgram program);
    [DllImport("libc", EntryPoint = "socket", SetLastError = true)] private static extern int ProbeSocket(int domain, int type, int protocol);
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)] private static extern long ProbeExec(long number, IntPtr file, IntPtr argv, IntPtr env);
    [DllImport("libc", EntryPoint = "mmap", SetLastError = true)] private static extern IntPtr ProbeMap(IntPtr address, nuint size, int protection, int flags, int descriptor, long offset);
    [DllImport("libc", EntryPoint = "munmap")] private static extern int Unmap(IntPtr address, nuint size);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "close")] private static extern int Close(int descriptor);
    public static void AssignScratchOwner(string path)
    { if (!Supported || Chown(path, 1654, 1654) != 0) throw Unavailable(); }
    [DllImport("libc", EntryPoint = "chown")] private static extern int Chown([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint user, uint group);
}
