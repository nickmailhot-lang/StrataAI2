# Permission-order state component diagnostics

This supports the [operational MVP milestone](operational-mvp-milestone.md) and extends the [state investigation](watch-order-state-investigation.md). The hosted fingerprint failure remains unresolved.

## Retained failure and diagnostic contract

[CI run 38287805799](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38287805799), commit `1d03b0c9ec56d50c336f319099fe37922a174739`, finishes its fourth full browser shard with four permission-order failures. MEMBER and PUBLIC_READER fail the first replay-state comparison; ADMIN and ORGANIZATION_READER fail the later read-order comparison. Exact preceding receipt-body comparisons pass. Retained container logs and traces do not establish which fingerprint component changed.

The browser scenario retains the exact original `jsonb_build_object(...)::text` expression, all nine tables, row ordering and MD5. One SQL statement now also derives component hashes from that same snapshot. All three original full-state comparisons still require equality. On mismatch only, `permission-state-component-hashes` attaches the role, before/after component hashes and changed field names. It contains no record contents, identifiers, request bodies, credentials or job payloads. The original assertion still fails. No retries, job draining, paused Worker, removed fields or relaxed comparisons are introduced.

## Verification, October 12, 2026

- Expanded pre-change private capture, with all production-default discovery loops enabled: **4/4** roles pass, zero skipped/flaky/unexpected, **466,062.785 ms**; each role records 20 full private snapshots and no checked replay-pair mismatch. No configuration jobs appear even with configuration discovery enabled. This does not reproduce or explain the hosted failure.
- Ordinary changed scenario: **4/4** roles pass, zero skipped/flaky/unexpected, **465,710.802 ms**, using the original 180-second case deadlines and pacing.
- Separate deliberate failure probe: change one disposable work-event job's `last_error_code` after the committed snapshot, then execute the original replay and comparison. The one MEMBER case fails the original assertion as required; exactly one attachment reports `changedFields: ["jobs"]`. Its nine before/after values are validated as hashes only. Probe duration: **38,262.821 ms**. This expected fixture failure is not counted as a passing product scenario and is not evidence of the hosted failure's cause.
- Browser types and **213** CI guard checks pass. A mechanical comparison verifies the complete original SQL expression is byte-for-byte preserved and all three comparison sites remain. Each capture follows a terminal successful fresh solution build.
- Independent final checks for both changed captures: **2,092** hosted source files, **1,414** native source files and all copied web assets match Primary. The failure probe reuses ordinary native runtime copies after verifying all 22 API and 35 Worker files match a fresh build. Primary remained frozen during captures.
- Owned containers, database, roles and private credential files were removed; original stopped PostgreSQL and its volume remain intact.

Private reports remain outside the repository under `ci-watch-component-diagnostics-hosted/native-browser-live1-20261012` and `ci-watch-component-failure-hosted/native-browser-live1-20261012`. Mounted compiled builds and disposable account activation do not certify immutable images or managed mail. Current full CI, a retained release bundle, and original PRD acceptance remain pending. Estimated PRD-17 work remaining: **15%**, unchanged; the issue remains open.
