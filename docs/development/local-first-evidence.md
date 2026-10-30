# Local-first implementation record — 2026-10-29

Owner: PRD-25-VS-02, supporting ARCH-11. Base main ff4c17121d3f66c43c7a3319914c7c6580ee1737.
This change affects developer tooling, not product routes, permissions or schemas.
UXA-02/03/04/07/08 are demonstrated through the CLI consumer: claim, verify, refuse
invalid/stale work, inspect the recorded outcome and hand off for integration.
Product viewport/visual review is not applicable to these CLI files; existing
product browser/security checks remain intact and are executed by the local gate.

## Baseline and setup

On this ARM64 Mac the initial workspace was empty and tools unavailable on PATH.
Installed Node 24.22.2 (verified published SHA-256), npm 11.19.0, .NET SDK 10.0.403
under the global.json latestFeature policy, Apple Command Line Tools, user-local
Docker Desktop, jq, GNU Bash 5.3 with published patches, OpenSSL 3.5.9 and
GNU coreutils 9.12, Python 3.13.18 and Playwright Chromium. Existing shell-test harnesses now resolve the installed Bash
before deliberately restricting PATH in refusal fixtures; assertions are unchanged. Locked npm/.NET dependency restores
passed. Baseline repository contracts: 396 passed, zero failed/skipped, 67.8s.
Baseline web types passed and native Release compilation passed with zero warnings
and errors in 39.3s. These are separate baseline observations, not final-tree proof.

## New workflow consumer checks

`npm run test:local` initially passed seven tests in 4.9s, including two actual linked
worktrees, independent claims, overlap/shared-file refusal, claim retention/handoff,
stale/partial/untracked-source hook rejection, invalid-base refusal, real-index
preservation and preservation of existing custom hooks. The expanded ownership suite passed ten tests, and the 124 shell/certificate
contracts passed after resolving the installed Bash and installing OpenSSL
configuration. The full restricted persistence program also passed, including
concurrency, forced-RLS, original retry, lifecycle and Worker adapter contracts.
The Python backup run separately exposed six failures/one error under system
Bash, then five remaining failures because BSD ln lacks the required atomic -T
operation. Both are retained; Python harness resolution and GNU coreutils address
those environment causes without changing backup/restore assertions. The system Python 3.9 subsequently
refused hashlib.file_digest-based verification; the doctor now requires Python
3.11+ and a user-local maintained Python is installed. GNU wc formatting is
required by the unchanged invitation paging count assertion. The runtime
rehearsal also caught a Linux /proc UUID dependency; a narrowly scoped CSPRNG
adapter preserves fresh fixture identity on this host. Browser reports/traces
are copied before temporary workspace removal and included in hashed evidence.
These diagnostic runs precede the final tree. Final candidate results are recorded by the verifier rather than substituted with
this initial pass.

The first disposable database startup was interrupted after it stalled during
Docker's initial setup. Its original log was retained locally; no passing database
result was inferred. Subsequent actual results must be read from their own logs.
The initial broader source pass retained 712 passes and 26 failures: existing
release scripts required newer Bash/OpenSSL, and the metadata contract required
its existing delivery-test command to remain intact. Modern tools and a separate
additive local-workflow test command address those causes without weakening tests.
The initial Linux persistence adapter separately retained a missing embedded SQL
resource failure and an explicit-local-TLS/identity-role setup mismatch; it now
uses complete source inputs, the canonical migration stream and matching restricted
CI fixture credentials. No prior failed product evidence or delivery history is
rewritten.

## Final evidence and remaining qualification

Final source identity is the Git tree SHA in the verifier receipt. Each command
has status, wall time, exact argv and SHA-256 of its log; final receipts live in
Git's common directory outside the tested source tree. The PR reports that tree,
resulting commit and actual final results. This document does not predeclare a
future run passing. Final failures prevent committing/pushing through the hooks.

The shared runtime uses canonical feature-runtime shell checks, real restricted
PostgreSQL/API/Worker and unchanged selected browser scenarios. Fixed fixture ports
are serialized by an exclusive common lock; runtime projects, images, volumes and
reports remain unique. All 741 repository contracts also passed on the staged candidate before a
separate runtime rehearsal exposed a cached-platform mismatch. That in-progress
verification was explicitly cancelled and retained; it is not a final pass. The
runtime now pulls its required PostgreSQL platform explicitly before Compose.
No concurrent production fixture execution is claimed. An initial ARM64 Worker
image build refused compilation in the existing x86_64-only preview launcher.
The runtime now explicitly uses linux/amd64 emulation, preserving the launcher
restriction and warning-as-error build; native source/persistence evidence remains
separate.

This script/CI/manifest change conservatively selects full-sanity. A local feature
gate is not the entire hosted qualification matrix, a release bundle, product
acceptance or MVP completion. Hosted required-ci/full-sanity, independent review
and target-branch integration remain required. No parent PRD is closed. No speedup
percentage or parity with AMD64 hosted runs is claimed; compare warm measurements
on matching revisions after rollout.

## Completed platform diagnosis

The maintained Python with a canonical temporary root passes all 299 Python
contracts. All 14 developer-workflow checks pass. A targeted runtime diagnosis
using retained prior-tree images passes 14 stages, including 5 passed (3.2m),
permission/tenant withdrawal, configuration/retention and abuse-limit checks. Its
image tree and updated harness tree are recorded separately; it is not a final
verification receipt for this candidate.

One targeted diagnostic log was overwritten by reusing its output folder. Its
observed failure is retained as an explicitly labelled summary, not as a raw
transcript. Original full-run failure logs and cancelled verification records
remain separate. The shipped verifier uses unique per-run folders and preserves
browser traces, reports and screenshots before cleanup. Final candidate results
are recorded outside the tested tree and summarized in the PR.

The first final candidate passed 745 repository contracts and 3,074 web tests (24 existing deferred cases), but its native Mac API run failed scanner path and protocol-peer teardown checks. That failed run is retained. API source contracts now run unchanged in a local Linux SDK container, covering Linux-only private-file cases. Independent collection scheduling is bounded to one to avoid memory pressure on this 8GB Mac. No failed native result is relabelled as passing.

The monolithic Linux API run exhausted the default 4GB Docker VM and made its daemon unresponsive. Its failed run and raw log are retained; no API pass was inferred. The local API adapter now discovers the complete method inventory and runs every method in fresh bounded batches, retains each original XML/log, and refuses missing/duplicated methods, invalid counts, failed/errored/not-run cases and missing/extra result files. Build servers are stopped before testing. Hosted single-process qualification remains independent.

Linux xUnit discovery appended an ANSI reset after JSON; the strict parser refused it. Explicit no-colour output and a retained discovery payload corrected the adapter, with a real first-batch XML preflight. A later unchanged-tree candidate passed unit/source/persistence checks but failed during invitation runtime testing while power logs recorded maintenance sleeps at 18:30 and 18:46 local time. The final run uses `caffeinate -isu`: an initial FullWake plus AC system-sleep and idle-sleep assertions were confirmed. Earlier failed/cancelled runs remain distinct from the final passing receipt. Assertions, expiry checks and deadlines were preserved.

## PR #132 clean-checkout review — 2026-10-30

GitHub's selector contract failed because the broad `**/bin/` ignore rule omitted
`scripts/local/bin/cat` from the published tree. Earlier Mac results remain
observations of those runs; their ignored live adapter did not prove a clean
checkout could reproduce the runtime. The adapter is now explicitly tracked,
while generated binaries remain ignored, and runtime PATH resolves it only from
the materialized candidate. A regression executes the clean snapshot after
replacing the live fixture adapter with a failing implementation.

The same review found CodeQL alert 14 on generic shell dispatch. Canonical shell
bodies are now retained as exclusive script files and invoked using separate
arguments with `shell: false`; environment values and paths do not form an inline
shell command. Regression coverage retains literal hostile path/environment
values, refuses script overwrites and propagates nonzero shell status. Original
CI failure details are preserved in
`docs/delivery/evidence/pr132-ci-failures-20261030.json`. Fresh exact-tree local,
hosted security/full checks and review are required before integration.
