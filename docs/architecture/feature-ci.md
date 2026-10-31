# Feature CI and full qualification — ARCH-11

The 2026-10-24 user-approved CI change separates development feedback from release qualification. Existing functional assertions, authority boundaries, rate limits, zero-retry policy, browser deadlines and acceptance obligations are preserved. A feature check is scoped evidence; it is not full PRD/MVP acceptance or release qualification.

| Workflow | Trigger | Required result / purpose |
| --- | --- | --- |
| `feature-ci.yml` | PR updates to main, label changes, merge queue, manual | Stable `required-ci`: selector/contracts, affected web checks, critical security/persisted browser baseline and selected feature cases; conservative full expansion |
| `main-smoke.yml` | Each main push, manual | `demo-ready`: real PostgreSQL/API/Worker/web startup, restricted runtime roles, permission/tenant checks and complete persisted Board workflow on desktop/tablet/phone |
| `full-sanity.yml` | Nightly 01:00 America/Vancouver, manual, `v*` tag, risk-selected PR | All previous source/PostgreSQL/recovery/integration/browser/security/bundle checks, plus exact-revision qualification record |
| `feature-runtime.yml` | Reusable by feature CI and main smoke | Generated bounded runtime; the same immutable images and production fixtures as full sanity |
| `ci.yml` | Manual compatibility entrypoint | Calls full sanity; preserves historical delivery implementation paths and bookmarks |

The nightly schedule uses `0 1 * * *` with `timezone: America/Vancouver`, so it follows Vancouver local time across daylight-saving changes. Scheduled runs qualify the checked-out default-branch revision. They do not qualify commits merged afterward. GitHub scheduling can be delayed; use a manual run for an urgent release candidate.

## Test selection and dependencies

The 2026-10-30 testing follow-up names the same policy's three tiers explicitly:

| Tier | Local and hosted obligations |
| --- | --- |
| `docs` | Documentation/evidence contracts, immutable inventory/drift, delivery/history and secrets; no web/.NET/database/browser compilation or execution |
| `feature` | Mapped isolated feature: web/browser types, lint/build, critical and affected units, canonical restricted runtime/Board/permission/tenant baseline and whole affected browser files |
| `full` | Backend/schema/auth/tenant, shared infrastructure/contracts/dependencies/deploy/CI/tooling, unknown/broad changes or explicit full request; complete qualification remains required |

Both selectors use `feature-impact.json` and `selectImpact`; local untracked source
additionally expands to full so Git-based changed-test selection cannot omit a new
test. Renames consider both paths. The existing dependency/coverage review remains
conservative: no backend or real-provider mapping has been narrowed. Instruction
changes in AGENTS.md expand to full. Case-insensitive security patterns also cover
lowercase permission/client/contract file names.

## Equivalent automatic requests

Marking ready changes review state without requesting another feature CI run.
Opened/synchronize/reopened and label changes retain selection/contracts/secrets.
Equivalent automatic requests serialize behind the current run; only synchronize
may cancel an older active feature run. Manual workflow dispatch, deliberate run
attempts, merge queue, nightly/tag/release and main smoke keep their own checks.

`automatic-qualification.mjs` can reuse only an original completed successful
feature CI run. Its hashed identity contains the actual tested integration commit
and tree, source head, target base, repository, all workflow/script Git blob
identities and exact tier/boolean/browser/command coverage. Exact equality prevents
a feature result from qualifying full work. A new commit/base/policy or scope
requires fresh work. No successful prior evidence means fresh selected jobs.

The stable `required-ci` job downloads the current request, recomputes the identity
against checkout, checks selection outputs and revalidates the original run and
artifact SHA-256 through GitHub before accepting reuse. Expired, missing, failed,
cancelled, pending, mismatched or chained reused evidence cannot produce success.
API lookup failures select fresh work; aggregate revalidation failures fail closed.
Reused passes are identified in the summary and never become reusable originals.
Proof is emitted only after the strict selected-job aggregate, and usable only
after the entire original workflow succeeds. Dependency caches are unrelated.

Nightly/release full-qualification artifacts still certify their own exact SHA;
this feature-CI reuse record is never a promotion or product acceptance record.
Actual timings/coverage are recorded with validation evidence; no speedup is
claimed before measurement. This workflow/policy change itself selects full.

`scripts/ci/feature-impact.json` is the reviewed path/category policy. The selector uses a full local Git diff against the PR base, with merge-base semantics and no rename collapsing, pagination or changed-file API cap. Both sides of renames and deleted paths are considered. A missing/invalid diff or unknown path expands to full sanity. A `full-ci` PR label or manual full input can expand coverage, never reduce it. Merge queue events conservatively run full qualification.

Documentation-only PRs retain CI/ledger/metadata contract checks and secret scanning. Code PRs retain the real persisted Board browser workflow and the existing permission matrix/discovery shell checks, including forbidden writes, cross-tenant isolation and withdrawal. The baseline case files run intact, including all viewports and assertions.

Mapped leaf frontend changes run web/browser typechecking, lint, production compilation, a fixed unit baseline and Vitest's changed/import-graph selection. The policy selects whole relevant Playwright files, not grep-filtered individual assertions. Categories are ordered from specific to generic. Broad selections above twelve files escalate to the parallel full matrix instead of a long single-runtime job. Keep this cap conservative; do not reduce coverage to meet a time budget.

Backend source, database/migration, auth/Organization, shared app/theme, frontend infrastructure, dependencies/locks, deployment, scripts, CI and browser fixture/helper changes run full qualification initially. Unknown feature files also expand. Real-provider features (attachments, notifications/reminders/watches, assignments, retention) expand to the full fixtures, including real Worker delivery. This deliberately conservative first map can be narrowed only after explicit dependency/coverage review and comparison against full runs. New backend feature mappings require equivalent real persistence/role/Worker evidence; filename matching alone is insufficient.

The strict aggregate checks selector success, valid boolean outputs, success for each selected job, and `skipped` only for jobs explicitly unselected. Cancelled, failed, missing or unexpectedly skipped checks fail `required-ci`. Selection evidence records the source revision, changed paths, selected features/files, expansion reasons and suites deferred to full sanity.

## Runtime, caches and release identity

The critical smoke uses real images, so code PRs still build three images once with cached layers. It omits bundle assembly and the full recovery/scanning/browser matrices for eligible leaf changes. No production/runtime cache is substituted for current source compilation or test results. Node caches dependency downloads keyed by the lockfile; .NET full jobs cache NuGet downloads against all package locks. Docker BuildKit uses per-image caches and exact current build arguments; image metadata/provenance is checked before export. Compressed image archives use zero artifact recompression. Superseded feature PR runs cancel; main smoke and qualification evidence stay bound to each distinct SHA.

Generate the runtime after editing full-sanity fixtures:

```sh
node scripts/ci/generate-feature-runtime.mjs
node scripts/ci/generate-feature-runtime.mjs --check
node --test tests/feature-ci.test.mjs tests/integration-suites.test.mjs
node scripts/ci/verify-integration-suites.mjs
```

The generator copies metadata/image identity, common service setup, auth/provider routing, baseline security commands, diagnostics and cleanup. Full steps remain authoritative. Its drift guard runs in feature and full CI. The browser executor validates selected paths against the actual inventory and critical baseline, then uses fixed executable/argv without a shell. Browser pacing and no-retry configuration remain unchanged.

Only full sanity assembles the runnable release bundle. The final `full-qualification-<sha>` artifact is emitted after source checks, full security/integration, bundle integrity and all three bundle startup modes pass. It records the exact revision, workflow run/attempt, image provenance and SHA-256 of the bundle checksum manifest. A failed/cancelled run cannot emit successful qualification evidence through that job. Main smoke artifacts have no qualification record and are not releases.

## Promotion and nightly failure policy

Before shared-demo deployment, require successful `demo-ready` for the exact main SHA. Before production/release promotion, require the successful full-sanity run and qualification record for the exact candidate SHA, and deploy that run's unchanged bundle/images. Never qualify a later SHA with an earlier nightly pass, rebuild images after qualification, or use a PR merge SHA as proof for a different main commit. Release tags rerun the complete qualification with their release version.

A nightly failure blocks release promotion until a new full run qualifies the candidate. Retain the original failure/evidence and triage product versus fixture/infrastructure failure. Fix or revert critical main regressions promptly; independent feature branches can continue. GitHub Actions failure notifications provide the alert; no external messaging integration is installed by this change.

There is currently no automated shared-demo or production deployment workflow in this repository. `demo-ready` and the qualification artifact expose the promotion requirements, but an external deployment must explicitly consume them. Do not describe a passing source/feature check as permission to promote.

## Rollout and duration targets

This CI-policy PR itself expands to full sanity. Keep `required-ci` as the PR branch-protection status name; the nested full suite reports `full-sanity-gate` to avoid collision. If repository rules require the old workflow path or individual full-job contexts, switch those settings to the feature aggregate after the comparison below. Preserve any unrelated protections. No admin settings are silently relaxed.

Compare several representative leaf changes and a shared/security change against manually requested full sanity at the same revision. Record pass/fail parity, wall time, queue time and longest job. Only then narrow additional mappings. The initial PR target is 5–10 minutes for small leaf changes; main smoke has the same target. Broad/security changes deliberately remain slower. These are targets, not measured hosted results. A 30-minute bounded runtime timeout catches hung fixtures without changing test assertions or per-case budgets.

Historical evidence remains historical. Feature-verified, full-qualified, product-accepted and MVP-ready remain distinct states.
