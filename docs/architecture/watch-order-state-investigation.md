# Current watch permission-order state investigation

See [component diagnostics](watch-order-component-diagnostics.md) for the expanded discovery capture and validated failure-only hash attachment. The hosted failure remains unresolved.

The `baad6b8c` full-browser fourth shard fails all four role scenarios at the
whole private-state fingerprint following original Card receipt replay. Source
mapping shows the preceding HTTP receipt-body comparisons passed; the failure
does not establish a changed receipt body. The fingerprint includes Cards,
watches, Work/audit events, background jobs, notifications, private journals,
streams and replay rows. Its full field coverage remains unchanged.

A private diagnostic captures the same atomic SQL object and returns its original
MD5, retaining all commands, fingerprints, observed Board-lock waits, authority
checks, single/bulk read outcomes and original replay checks. All MEMBER, ADMIN,
ORGANIZATION_READER and PUBLIC_READER scenarios pass together in 466,082.616 ms.
The complete ordinary repository file, without diagnostic instrumentation,
passes all four in 462,809.883 ms. Both have zero failures, skips or flaky cases.
These local passes do not identify or repair the hosted hash mismatch.

Both final captures independently have zero mismatches across 2,087 hosted
source/runtime hashes, 1,414 native source hashes and four web assets. A fresh
zero-warning solution build precedes capture. All owned containers, temporary
database/roles and credential files are removed, preserving original stopped
PostgreSQL data. Unused copied Windows native symbols are excluded; Linux
runtime inputs and source/report evidence remain intact.

Snapshots show automatic metadata/recipient-authority jobs succeeded and general
Work delivery jobs pending. The private topology excludes configuration discovery,
but snapshots contain no configuration-delivery jobs; that difference therefore
does not explain the failure. No jobs-only transition was identified in the
instrumented captures. A legitimate independent Worker transition is still a
hypothesis, not a proven cause. Do not remove mutable fields, weaken hashes or
close the issue based on these results.

The fixtures use verified disposable accounts, real restricted PostgreSQL/RLS,
the compiled API and separate Worker. Fixture account activation does not prove
managed mail delivery, and mounted runtimes/assets do not certify the current
build-once retained candidate. Current complete immutable CI and the broader
[notification acceptance map](prd-17-acceptance.md) remain necessary. Estimated
PRD-17 work remaining stays **15%**.

The refreshed 2026-10-12 [dependency audit](../ticket-dependencies.md) still
contains 92 canonical definitions, 91 open tickets and three dependency cycles.
All 92 requirement-body hashes match the prior inventory; no ticket was closed.

## Hosted authority-wait setup correction, October 13

The latest failed [CI run 38452533316](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38452533316), at `a6301cb3`, fails all four permission roles at the added wait for original `INVITATION_RECIPIENT_AUTHORITY_PAGE` jobs to reach `SUCCEEDED`. The job-wide fixture environment disables recipient-authority discovery, and the strict watch-order step had not enabled it. Production Board/Organization authority producers still enqueue those pages. The required real dispatcher was therefore absent from the hosted setup, while earlier local captures used the enabled discovery loop.

The strict watch-order step now enables only recipient-authority discovery, recreates the already-built immutable Worker with the existing identity fixture overlay, and verifies its effective flag before running the browser matrix. An exit trap restores the job-wide disabled discovery setting even after failure. Issuer discovery, general Work job scope, source/permission assertions, original complete hashes, the 30-second delivery wait and 180-second case limits are unchanged. No rows are rewritten to simulate delivery, and no browser case or mutation is retried.

Workflow lint, browser TypeScript and 222 CI/startup guards pass. All five watch-order cases and the two List-position cases are discovered; actual exact-image execution is still pending. This corrects the observed dispatcher setup failure; it does not by itself prove that every earlier replay-state mismatch is resolved. No large-load tests are enabled. Estimated full PRD-17 work remaining stays **15%**, and PRD-05 stays **14%** (provisional); both issues remain open.

The restoration trap now captures the active authority Worker's logs before recreating its container. Failure diagnostics select that separate `watch-order-worker.log` through the canonical evidence inventory, alongside the current `compose.log`, build identity and checksums. Restoration therefore retains the dispatcher's diagnostics even though the later compose capture observes a different Worker container. A staging regression verifies both distinct byte streams and hashes after current-container log replacement. Eleven evidence tests pass; the platform-specific symlink test is skipped on Windows. All 222 CI/startup guards and workflow lint pass. Exact-image execution remains pending; the original failure assertions and phase settings are unchanged.
