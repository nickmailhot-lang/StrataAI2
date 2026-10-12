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
