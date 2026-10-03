#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <linux/landlock.h>
#include <stdint.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <sys/resource.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <unistd.h>

/* Single-threaded, fixed release-image launcher. Landlock is inherited by
 * every CLR/native thread because it is applied before the initial exec. */
static int fail(int code) { fputs("Isolated preview launcher unavailable.\n", stderr); return code; }
static int limit(int resource, rlim_t value) {
    const struct rlimit bound = { value, value };
    return setrlimit(resource, &bound);
}
static int allow(int ruleset, const char *path, uint64_t access) {
    const int descriptor = open(path, O_PATH | O_CLOEXEC);
    if (descriptor < 0) return -1;
    const struct landlock_path_beneath_attr rule = { .allowed_access = access, .parent_fd = descriptor };
    const int result = (int)syscall(SYS_landlock_add_rule, ruleset, LANDLOCK_RULE_PATH_BENEATH, &rule, 0);
    close(descriptor);
    return result;
}
int main(int argc, char **argv) {
#if !defined(__x86_64__)
    (void)argc; (void)argv; return fail(65);
#else
    if (argc != 2 || getuid() == 0 || geteuid() == 0) return fail(65);
    const char *prefix = "/tmp/strata-preview-sandbox-";
    if (strlen(argv[1]) != strlen(prefix) + 32 || strncmp(argv[1], prefix, strlen(prefix)) != 0) return fail(65);
    for (const char *p = argv[1] + strlen(prefix); *p; ++p)
        if (!((*p >= '0' && *p <= '9') || (*p >= 'a' && *p <= 'f'))) return fail(65);
    struct stat scratch;
    if (lstat(argv[1], &scratch) || !S_ISDIR(scratch.st_mode) || scratch.st_uid != geteuid()
        || (scratch.st_mode & 0777) != 0700 || chdir(argv[1])) return fail(65);
    const pid_t parent = getppid();
    if (prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) || prctl(PR_SET_DUMPABLE, 0, 0, 0, 0)
        || prctl(PR_SET_PDEATHSIG, SIGKILL, 0, 0, 0) || parent != getppid()
        || limit(RLIMIT_AS, 1073741824) || limit(RLIMIT_CPU, 10) || limit(RLIMIT_CORE, 0)
        || limit(RLIMIT_FSIZE, 1073741824) || limit(RLIMIT_NOFILE, 64) || limit(RLIMIT_NPROC, 64)) return fail(66);
    /* ABI v3 includes truncate; do not silently accept a weaker filesystem
     * boundary on older kernels. No provider input is consumed on failure. */
    if (syscall(SYS_landlock_create_ruleset, NULL, 0, LANDLOCK_CREATE_RULESET_VERSION) < 3) return fail(67);
    const struct landlock_ruleset_attr policy = { .handled_access_fs = 32767 };
    const int ruleset = (int)syscall(SYS_landlock_create_ruleset, &policy, sizeof(policy), 0);
    if (ruleset < 0) return fail(68);
    const uint64_t read = LANDLOCK_ACCESS_FS_READ_FILE | LANDLOCK_ACCESS_FS_READ_DIR;
    const uint64_t code = read | LANDLOCK_ACCESS_FS_EXECUTE;
    const uint64_t scratch_access = read | LANDLOCK_ACCESS_FS_WRITE_FILE | LANDLOCK_ACCESS_FS_REMOVE_FILE
        | LANDLOCK_ACCESS_FS_MAKE_REG | LANDLOCK_ACCESS_FS_TRUNCATE;
    int bad = allow(ruleset, "/app", code) || allow(ruleset, "/usr/share/dotnet", code)
        || allow(ruleset, "/lib", code) || allow(ruleset, "/usr/lib", code)
        || allow(ruleset, "/proc/self", read) || allow(ruleset, "/proc/meminfo", LANDLOCK_ACCESS_FS_READ_FILE)
        || allow(ruleset, "/proc/cpuinfo", LANDLOCK_ACCESS_FS_READ_FILE) || allow(ruleset, "/sys/fs/cgroup", read)
        || allow(ruleset, "/etc/ld.so.cache", LANDLOCK_ACCESS_FS_READ_FILE)
        || allow(ruleset, "/dev/urandom", LANDLOCK_ACCESS_FS_READ_FILE) || allow(ruleset, argv[1], scratch_access);
    if (!bad) bad = (int)syscall(SYS_landlock_restrict_self, ruleset, 0);
    close(ruleset);
    if (bad) return fail(68);
    char *const command[] = { "/usr/share/dotnet/dotnet", "/app/StrataAI.Worker.dll", "--decode-private-attachment-preview", NULL };
    execv(command[0], command);
    return fail(69);
#endif
}
