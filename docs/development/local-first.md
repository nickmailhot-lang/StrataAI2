# Local-first development — PRD-25 / ARCH-11

Develop and verify on your machine, then commit and push a feature branch. GitHub
remains the independent review, required-ci and exact-revision release authority.
This tooling does not close PRD-25 or certify the MVP.

## Setup on this Mac

Use Node 24, the exact npm declared in package.json, the .NET SDK policy in
global.json, working Git, Docker with Compose, Python 3.11+, jq, Bash 5+, OpenSSL 3+, GNU coreutils and Playwright Chromium.
Apple Command Line Tools supply native build tools. The system Python 3.9 lacks
hashlib.file_digest used by backup verification; this installation uses a separate
user-local Python 3.13.18. The system Bash
3.2 and LibreSSL cannot run the existing Linux-oriented release contracts; install
modern Bash/OpenSSL ahead of them on PATH. This Mac uses user-local GNU Bash
5.3 with published patches, OpenSSL 3.5.9 and GNU coreutils 9.12. GNU ln,
sha256sum, timeout, date, stat, readlink and realpath are in ~/.local/bin; the
backup contract deliberately requires GNU ln for atomic no-overwrite publication.
The full GNU coreutils bin directory also precedes BSD utilities on PATH, matching
Linux fixture formatting such as wc output. The doctor checks this requirement. Docker Desktop
must be running. PowerShell is optional: the new Linux persistence runner uses
Node and Docker on every host, preserving the production Linux file checks.

This installation uses user-local Node/.NET under ~/.local/share/strataai,
commands in ~/.local/bin, and Docker.app in ~/Applications. ~/.zprofile adds the
paths and DOTNET_ROOT. Open a fresh login shell (`exec zsh -l`) after setup.
No repository script installs global software or accepts licenses automatically.

From the repository root:

```sh
npm ci
npx playwright install chromium
npm run dev:doctor
npm run dev:hooks
npm run dev:agents -- init --owner integrator
```

The doctor reports actual versions, architecture and Docker daemon readiness.
It requires a local socket Docker engine. Runtime fixture authority/provider
settings come from the canonical CI job; ambient STRATAAI/COMPOSE settings are
excluded. Its SDK check runs dotnet in the repository, so global.json controls resolution.
Docker Compose v2 or newer is required. Keep dependencies locked; repeat npm ci
only after changing lockfiles or in a newly created worktree.

Official installers: [Apple Command Line Tools](https://developer.apple.com/xcode/resources/),
[Node 24](https://nodejs.org/en/download), [.NET](https://learn.microsoft.com/dotnet/core/install/macos),
[Docker Desktop](https://docs.docker.com/desktop/setup/install/mac-install/),
[jq](https://jqlang.org/download/), [GNU Bash](https://ftp.gnu.org/gnu/bash/),
[OpenSSL](https://openssl-library.org/source/), [GNU coreutils](https://ftp.gnu.org/gnu/coreutils/). Verify downloaded checksums before installation.

## The local gate

```sh
npm run verify:local -- --base origin/main --plan
npm run verify:local -- --base origin/main --mode fast
npm run verify:local -- --base origin/main
```

Plan mode includes committed branch changes against the merge base, staged,
unstaged and untracked files, deletes and both rename paths. It reuses
scripts/ci/feature-impact.json and selectImpact; invalid bases and missing critical
browser inventory refuse selection. Unknown paths conservatively select full CI.

Fast mode runs the contract tests declared by feature CI, history-preserving ledger validation,
integration inventory/runtime drift checks, types, lint, the same critical unit
baseline and changed/import-graph selection as feature CI, and a production web build. It never issues a commit receipt.

Final mode expands root contracts, all web units, locked .NET restore/Release
compilation with warnings as errors, native domain tests, Linux API tests, all Python contracts and the
complete Linux restricted persistence program when full impact is selected. Eligible
leaf changes retain the CI critical unit baseline plus changed/import-graph tests
and CI release-artifact contracts. Untracked source expands to complete local
coverage, preventing Git-based selection from missing new files. Every final run
scans the candidate for secrets; every code change runs the canonical generated
feature-runtime shell checks plus selected whole browser files. `--all-browser` explicitly expands to every browser spec. The unchanged
Playwright configuration retains one worker, zero retries and original deadlines.
Web unit tests run with one worker to reduce resource contention on laptops.

**A local feature pass is not full qualification.** The full-impact flag and reasons
remain in the receipt and still require GitHub full-sanity before merge when the
existing policy selects it. Local feature runtime does not reproduce every full
PostgreSQL upgrade/recovery, provider, scanner, SBOM or source-free release-bundle
job. It must never emit a full-qualification artifact. The nightly/release/MVP
rules in [feature CI](../architecture/feature-ci.md) remain unchanged.

The runtime executes checked-in feature-runtime shell bodies directly, replacing
only hosted artifact transfer with current-tree local image builds, hosted browser
installation with the prepared local browser, and fake-provider launch/cleanup
with an owned process. A runtime-only cat adapter supplies a Node CSPRNG UUID for
the Linux /proc/sys/kernel/random/uuid fixture read; all other reads go unchanged
to system cat. No production image, assertion or authority rule is changed. It retains real PostgreSQL, restricted roles, API, separate
Worker, production browser fixtures, rate pacing and permission checks. This is
trusted repository code: review CI/script changes before execution.

The candidate is materialized from its exact Git tree into a disposable temporary
workspace for Docker bind mounts, avoiding protected Documents-folder access and
concurrent edits. Git checkout-index preserves files even if archive export-ignore
attributes exist. Source, database and test changes invalidate the final receipt.

Images are built once per local run with unique tags and Docker layer caches.
Build labels identify the tested Git tree and a local version, not a fictitious
CI commit or release. Architecture is recorded. The current preview launcher deliberately supports
x86_64 only, so canonical runtime images use linux/amd64 (emulated on this Mac).
Native source/persistence checks remain separately identified. Emulation may be
slower and does not itself certify a hosted AMD64 release.

## Evidence and commit/push rules

Receipts, browser traces/screenshots/reports and logs live outside the source tree under the Git common directory's
strataai-local/runs folder. Inspect the path printed after each command. Failures
retain their original logs; rerunning never converts them into passing history.
Verification subprocesses receive a canonical temporary-directory path, preserving
strict symlink refusal checks despite macOS /var aliases. Nested retained artifacts are hashed as well as command logs. The private runtime
environment is excluded from the receipt inventory to avoid self-reference or
credential publication. Temporary credentials and provider diagnostics are local private files; do not
upload raw runtime environment files.

The verifier uses a temporary index to hash all nonignored candidate content,
including executable bits and new files, without modifying the real index. It
checks the snapshot, HEAD and base again when testing ends. Any changed source
invalidates that run. The pre-commit hook requires staged content, working content
and the final passing tree to match. Partial staging and new untracked source
therefore refuse commit. Stage intentionally and reverify after any edits.

The pre-push hook checks the same tree and hashed command evidence, refuses main,
branch deletion, non-HEAD refs and non-fast-forward updates. Never use --no-verify,
force-push, stage another agent's files or modify evidence to obtain a pass. Hooks
are local guardrails, not tamper-proof security; GitHub checks remain mandatory.
Existing custom hooksPath settings are preserved and require explicit integration.

A final pass can be committed without rerunning identical tests: the commit changes
its identity, while its tested tree stays identical. Any integration conflict fix
or combined source change requires new verification before its integration commit.
Do not mark the delivery ledger Done until its existing review/acceptance/ancestry
gates actually pass.

## Parallel work and shared runtime capacity

Follow [multi-agent coordination](multi-agent.md). Every agent has its own branch,
worktree, node_modules and .NET outputs. Do not share writable build directories.
The registry and locks live in Git's common directory, shared by linked worktrees.
They coordinate one machine; separate clones/machines need one external coordinator.

The current immutable CI fixtures contain fixed host ports. A common exclusive
runtime lock serializes those checks across worktrees and checks for occupied
ports before startup. It never kills an unrelated service. Every run still has a
unique Compose project, image tags, database volumes and evidence directory.
Independent editing/source checks continue while the runtime slot is occupied.
Changing fixture ports throughout the product suites is deliberately deferred;
there is no claim of concurrent production runtime suites on this machine.

On failure, runtime volumes and persistence databases are retained for inspection.
Stop only your recorded project/container after diagnosis; never run global Docker
prune/cleanup. Locks record their owning PID, worktree and timestamp. A crashed lock must be investigated by the coordinator and removed
only after confirming the owning process has stopped. Locks are never stolen by age.

## Rollout and measurement

The developer-tooling workflow itself is selected for full CI under the existing
policy. Local feature evidence is a prerequisite to its commit/push, with hosted
qualification still pending afterward. Use [the implementation record](local-first-evidence.md)
for observed results and limits. Measure cold setup separately from warm verification;
record source snapshot, queue time, wall time and longest check before changing
coverage or claiming a speedup.

## Publishing through a connected GitHub account

Terminal Git authentication and a connected GitHub account are separate. If an
agent uses the connected GitHub API to publish, Git hooks do not execute remotely:
the agent must run the local hook checks explicitly, publish only the exact
verified tree and intended parent, use a new branch or expected-head update, and
fetch/compare the resulting tree afterward. Preserve any original local commit
if the API supplies different commit metadata; metadata changes cannot authorize
a different source tree. Never treat connector access as a verification bypass.

API contracts run in the local Linux SDK container so production private-file checks execute and Linux socket semantics match CI. API methods are discovered from the built assembly and all run in bounded groups of 20, each in a fresh xUnit process, with independent collection scheduling disabled. XML results must cover the complete discovered inventory exactly once across batches, with no failed, errored or not-run cases. Existing skips remain separate. Test bodies, internal concurrency, deadlines and assertions are unchanged. This bounds memory on this 8GB Mac; the original hosted single-process suite remains required. The immutable source snapshot is copied to disposable container storage, keeping host bin/obj files separate.

On this Mac, use `caffeinate -isu npm run verify:local -- --base origin/main` to enter FullWake and prevent system/idle sleep while verification runs on AC power. Sleep can consume command deadlines and distort wall-time measurements.

Preserve every ordered merge parent when publishing a merge commit through an API; use a single-parent API call only for a single-parent commit.

For an evidence/documentation follow-up, retain the full passing receipt for the code parent and verify the documentation tree against that qualified commit with `--base COMMIT`. Record both source identities and scopes. Executable-source or CI changes use the ordinary `origin/main` comparison; PR CI still compares the complete branch against its target. This follow-up cannot qualify previously untested code.
