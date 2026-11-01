# Development feedback within ten minutes

The user approved this delivery policy on 2026-10-31. Ordinary pull requests use
feature checks; full regression remains nightly and mandatory for each production
candidate. This changes scheduling and the meaning of a development pass. It does
not remove original tests or turn partial evidence into product acceptance.

PR134 completed its original qualification unchanged and merged normally as
`43c0446212c7976f94d0d141e43abcd9e3313a2c`. Its source, assertions, failures,
receipts and backups remain separate. This work continued in its own managed
worktree after verified target integration and serial ownership transfer.
Existing ARCH-11-VS-94 and PRD-02-VS-02 records are reconciled, not duplicated.

## Gate contract

- Frontend changes select web/browser types, lint, production compilation,
  affected component tests, critical security units and focused UI smoke. They
  do not run the entire .NET API suite.
- Backend changes retain compilation, fast domain tests, critical HTTP authority
  checks and explicitly selected feature API tests. The complete 45-minute API
  regression suite belongs to nightly/candidate qualification.
- Documentation keeps contracts/history validation and secrets without compilers.
- CI/helper changes validate their own selection/execution/refusal contracts.
- The existing dependency map identifies affected features and deferred production
  browser coverage. Unknown paths refuse selection until dependency review.
- Every selected job must succeed; missing, cancelled, failed or unexpectedly
  skipped jobs cannot produce `required-ci`. Unselected jobs are explicit.
- The candidate revision and exact selected scope must match. Qualification reuse
  must include the new profile and all API/compile/smoke selections before enablement.
- The execution budget is below 600,000ms. An overrun fails the gate and retains
  evidence; it never silently drops a suite or lengthens assertions. Runner queue
  time is recorded separately. A timeout is not evidence of faster execution.

## Full testing and promotion

The full suite keeps its original assertions, roles, tenant/Portal isolation,
fixed BoardWindow counts, native keyboard recovery, fixture ownership, per-case
deadlines, zero retries, providers and immutable bundle checks. Nightly results
qualify only their own revision. Production promotion requires a successful full
run for the exact candidate and uses its unchanged qualified artifacts. Failed
nightlies block promotion and retain their original failure history.

The accepted tradeoff is later discovery of some cross-feature regressions after
merge. Fast feature results must name deferred suites and cannot certify full
functional/UX/MVP acceptance. Heavy runtime/image matrices must not accidentally
return to ordinary PR gates through a broad path escalation.

## Validation still required

The branch now wires parallel selected runners and the strict aggregate into the
feature workflow, with matching local verification and guarded exact-tree receipts.
The web selector resolves the application's TypeScript dependency graph, including
literal dynamic/mock imports, and adds existing critical admission, navigation,
authentication and Problem-boundary tests. Removed sources and dependency/config
changes select all web unit tests. API selection retains complete methods from
reviewed files, including every theory row; it avoids selecting the shared partial
class that would accidentally run the entire API regression. Its TRX validator
refuses missing methods, skipped/failed cases, duplicate executions, unexpected
methods and inconsistent counters, and omits private theory arguments from output.
The first complete web-runner attempt executed 3,103 passing cases and the 24
existing capacity deferrals, then failed its new report consumer's incorrect
assumption that parameterized display names are unique. The failed run remains
failed. The corrected consumer matches every row against independent runtime
discovery multiplicities, retaining source-bound deferrals separately and applying
XML attribute whitespace normalization to one unchanged unsafe-URL title. Actual
retained results matched the independent inventory; no assertion/case was removed.

The shared Linux backend runner separately built the complete solution, passed
1,105 Domain cases with three existing load deferrals, and passed all 47 cases in
17 reviewed HTTP methods in 98.741 seconds. This is scoped local Linux source
evidence, with source/tree and report guards, not production persistence acceptance.

The exact executable tree `3188f6d5` subsequently passed the seven-stage final
local feature gate in 398.629 seconds: all 3,103 web cases with the original 24
capacity deferrals, compilation, types/lint, compiled two-width UI smoke, contracts,
ledger/inventory/drift and secrets. A separate exact-tree Linux backend lane passed
in 99.483 seconds with strict Domain and HTTP report consumers; complete JavaScript
CodeQL found zero findings across 87 rules. Original failed attempts remain retained.
Actual hosted frontend/backend/tooling timings and independent review remain required. Manual measurement can add backend coverage while retaining
all actual changed-source checks; cold runs use fresh dependency caches. Record
execution and queue/setup separately. No under-ten-minute hosted result is yet claimed.

The compiled sign-in smoke initially exposed missing main/region landmarks at
390px and 1280px. Both failures and the earlier locator setup failure are retained.
The separately authorized correction uses the existing MUI Container's `main`
element without changing authentication, recovery or layout. Both original Axe
and same-key/body keyboard retry checks now pass, alongside 77 account-entry and
invitation component cases, build/type checks and lint. This is scoped local
evidence; it does not change PR134 or establish hosted timing/product acceptance.

## Retained first hosted timing failure and bounded recovery

The first PR135 feature run (`41275468371`, head `68d891bf`) passed its complete
web lane: 225 files, 3,103 cases and 24 existing capacity deferrals, compilation,
types/lint and both compiled UI scenarios. The lane took 494.568 seconds, but the
whole feature interval exceeded ten minutes and the strict aggregate correctly
failed. The cold measurement (`41275507572`) passed its backend lane in 153.644
seconds, including all 47 reviewed HTTP cases and 1,105 Domain cases with three
existing deferrals. Its web job timed out during unit execution. Both failed
qualifications remain failed; their original ZIPs and logs are retained with
verified digests. No warm measurement or unchanged rerun was dispatched.

Runtime collection and execution repeat isolated file initialization. The bounded
recovery partitions the complete selected web files deterministically across three
hosted jobs. Each file and every original parameter row remains in exactly one
partition, with independent runtime discovery and strict JUnit validation. Sibling
jobs retain their evidence when one fails. Partition one also owns types, lint,
compilation and the unchanged compiled UI checks; these checks run once. The
aggregate requires all three source/scope-bound reports, hashes and revalidated
case inventories before it can pass. Missing, duplicate, empty, failed or altered
partitions refuse. The partition manifest is included in the sealed scope and
qualification reuse identity. The original ten-minute budget remains unchanged.

A meaningful old-workflow contract failed for the absent parallel execution and
report consumer before the recovery. Targeted execution of the shared runner used
all 11 dependent Home/critical files split 4/4/3, preserving all 184 cases and both
compiled UI cases. This scoped probe passed locally. The subsequent exact source tree `a30d60ea`
passed all seven final local feature stages in 397.688 seconds, including all 225
web files, 3,103 passing cases, the original 24 capacity deferrals, types/lint/build,
both compiled UI scenarios and secrets. Complete fresh JavaScript CodeQL found
zero findings across 87 rules. These results qualify code `ffce77d2` locally;
fresh hosted checks and cold/warm qualification are still required. Local final verification
continues to execute the complete selected web files on this machine, without
simultaneous heavy processes. No original production test/configuration or full
nightly/candidate workflow was changed.
