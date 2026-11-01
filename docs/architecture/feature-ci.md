# Feature CI and full qualification — ARCH-11

The user-approved 2026-10-31 policy targets a development feature gate below ten
minutes, retaining full testing nightly and for every exact production candidate.
This supersedes automatic full expansion for ordinary shared/CI/backend changes.
Original functional assertions, authority, retries, deadlines, fixtures and
acceptance obligations remain intact. Feature evidence is not release or MVP acceptance.

| Workflow | Trigger | Purpose |
| --- | --- | --- |
| `feature-ci.yml` | Main PR updates/labels, merge queue, manual | Stable `required-ci`: contracts/history/secrets, selected parallel web/backend lanes, strict exact-scope aggregate |
| `full-sanity.yml` | Nightly 01:00 America/Vancouver, `v*` tag, manual, explicit `full-ci`/full input | Complete original source/PostgreSQL/recovery/browser/security/immutable bundle qualification |
| `main-smoke.yml` | Main push, manual | Separate real persisted operational/demo evidence; never replaced by fixture UI smoke |
| `feature-runtime.yml` | Main smoke and explicit full/local consumers | Retained generated real-service fixture/permission/tenant baseline |

The feature-gate budget excludes initial runner queue/setup, which is recorded
separately. Its measured interval begins before dependency setup/contracts and
includes subsequent lane waits and aggregate work. An interval at or above
600,000ms fails; a timeout never proves a speedup. Full/nightly/production checks
are intentionally outside this budget. Hosted cold/warm timings must actually be
measured before claiming the target is met.

## Selection and execution

`ci/fast/plan.mjs` shares the existing dependency map but makes development scope
explicit. Full Git comparisons include both rename paths, deleted files, and all
local staged/unstaged/untracked files. Missing comparisons, unsafe paths, missing
critical inventory and unreviewed backend consumers refuse instead of producing
an empty pass. Ordinary CI changes validate their own refusal contracts; they do
not automatically start the 45-minute complete API suite.

| Profile/lane | Executed scope | Deferred scope |
| --- | --- | --- |
| Documentation | Contracts, ledger/history, immutable coverage/drift and secrets | Compilers/runtime suites |
| Frontend | Web/browser types, lint, production build, complete TypeScript-dependency-selected unit files plus critical auth/admission/Problem tests, compiled UI smoke | Complete canonical production browser/role/provider matrix |
| Backend | Locked whole-solution build with warnings as errors, complete Domain suite, complete reviewed HTTP methods and all theory rows | Complete API/PostgreSQL/Worker/provider/recovery matrix |
| Explicit full | Original complete full-sanity workflow and qualification record | No new development-policy omissions |

Removed frontend sources, helper/config/dependency changes conservatively select
all web units when the web lane is required. Critical files must exist. Hosted web files are divided deterministically into three complete-file partitions;
types/lint/build/compiled UI run once in partition one. Every partition retains
independent runtime discovery and strict reports. The aggregate verifies all three
source/scope-bound reports, hashes and case inventories before success; missing,
duplicate or failed partitions refuse. Local verification executes the complete
selected files serially on this machine. Runtime
Vitest discovery is compared with JUnit case multiplicities; repeated parameter
display names preserve every row. XML attribute whitespace is normalized only for
report identity. Empty reports, missing files/cases, failed cases and unreviewed
skips refuse. The fixed 24 BoardWindow functional counterparts and 24 original
source-bound capacity deferrals remain distinct; deferred is never passed.

API tests share partial classes. Selecting a class would accidentally rerun the
whole suite, so `backend-selection.mjs` selects reviewed complete files/methods,
retaining every original assertion and theory row. `run.mjs` shares commands with
local verification. Strict TRX checks cover actual selected methods, outcomes and
counters; the three original source-bound Domain load deferrals remain separate.
These HTTP/Domain checks do not certify real persistence or Worker delivery.

The compiled UI smoke runs the actual production build/router/theme with synthetic
HTTP refusals at phone and desktop widths. It preserves native keyboard retry,
exact request count/key/body, original result deadlines, zero retries and Axe
assertions. Canonical persisted journeys, all original viewports/roles/tenant and
Portal boundaries remain in the complete production suite. No synthetic result
substitutes for UX/product acceptance.

## Strict aggregate and equivalent requests

Only selected successful jobs can produce `required-ci`. Failed/cancelled/timed-out,
missing or unexpectedly skipped jobs refuse; unselected jobs must be skipped.
Manual backend measurement can only add a lane, retaining all actual changed-source
checks. `cold_cache` disables dependency-cache restoration and uses fresh package
folders. These inputs are explicit manual measurements, never PR overrides or
production qualification.

Automatic reuse still requires an original completed successful same-workflow run
and a digest-verified artifact. Schema 2 binds repository, tested integration
commit/tree, source/base, every checked-in workflow/script/`ci` policy blob and the
entire profile: selected web files, backend/API methods, smoke/deferred scope,
measurement inputs and scope fingerprint. Old schema/scope, missing timings,
over-budget feature proofs, changed policy/source/base and chained reuse refuse.
The aggregate recomputes the request and revalidates original run/artifact identity.
Missing lookup evidence means fresh checks; final revalidation fails closed.

Ready/body edits do not request duplicate feature runs. Automatic label requests
serialize; only synchronize can cancel an older feature run. Manual dispatch,
retries, merge queue and nightly/candidate requests remain fresh. Dependency caches
are independent of qualification reuse. Every feature proof explicitly records
`productionQualified: false`.

## Full qualification and promotion

The original nightly schedule follows America/Vancouver daylight saving time.
Nightly evidence qualifies its own checked-out revision, not later commits. Tags,
manual full requests and the `full-ci` label retain the full workflow. An exact
production candidate must pass its complete real-service, security and immutable
build-once bundle checks; feature receipts cannot replace that record or its
exact-source/artifact verification. Failed nightlies retain their history and do
not authorize promotion.

Full test assertions, original command/authority recovery, least-privilege roles,
Worker fixture restoration, capacity phases, privacy, browser retries/deadlines,
provider/scanner/SBOM and source-free bundle tests are preserved. Main smoke remains
separate post-integration operational evidence. No parent PRD/product acceptance
follows merely from a feature pass or merge.

The accepted development tradeoff is later discovery of some cross-feature
regressions. Report implementation, scoped tests, UX/product review and full/MVP
acceptance separately. Evidence and current limitations live in
`docs/architecture/fast-feature-ci.md` and the append-only delivery report. The
previous conservative automatic-full policy remains historical in commit
`43c0446212c7976f94d0d141e43abcd9e3313a2c`; its retained selector/runtime fixtures
still support the unchanged full/main-smoke suites.
