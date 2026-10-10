# Demo reminder delivery and audit persistence

This increment addresses ARCH-03/05/07 and the reminder paths in PRD-12/17.
Local API and native browser verification passes. Current immutable-image CI
remains required. This does not complete future modules, all Demo job handlers
or those tickets' acceptance.

## Runtime behavior

Normal Demo startup registers a process-local hosted reminder processor in the
API. It uses no PostgreSQL connection, mail provider or other external service.
Production keeps the separate Worker and existing PostgreSQL delivery capability.
The Demo processor selects only committed `CARD_REMINDER` jobs; unrelated job
types stay available to their own consumers. Organization discovery is internal,
bounded by the existing 10,000-job capacity, and rotates between due Organizations.
One advancement consumes at most one job. Concurrent advancements serialize.

Publication retains the original reference payload, idempotency key, schedule
and actor. Delivery checks the exact stored job, Worker, lease, expiry, attempt
and generation. It reads current recipient account, verification policy,
Organization membership, Board visibility/membership, active parents, due time
and completion state under the shared account and Work gates. Superseded work
produces no effect; a missing live lease cannot authorize delivery.

The private event, personal notification, `FIRED` transition and immutable Work
audit fact commit together. The event/notification/audit identity is the original
job ID. Recovery before acknowledgment keeps the first effect without another
notification or version increment. Late lease loss, exception or cancellation
restores all tentative effects and journal sequence. Failure uses the existing
30-second exponential backoff, five-attempt bound and terminal failure state.
No deadline or retry policy is increased.

Work audit writes now retain actual source references and clock values rather
than returning an empty task. Facts are private, append-only and participate in
owning transaction rollback. This storage has no new HTTP disclosure endpoint.
It is distinct from activity projections and diagnostic logs.

Demo data, jobs and audits belong to the API host lifetime. Restart discards them;
[the sample-catalog reset](runtime-modes.md) does not reset canonical accounts or
queues. Use the documented seeded Demo account for ordinary sign-in.

## Executed evidence and limits

The initial actual-HTTP regression creates a canonical reminder, advances its
real queue schedule and claims its published references, then fails on the absent
`ICardReminderDeliveryStore` registration. The baseline report is retained
privately. Three initial assertion-analyzer build errors were corrected without
suppressing analyzers or weakening assertions.

The locked solution builds have zero warnings/errors. The eligibility scope has
**37 actual unique focused cases passing**, with matching counters and zero
failed, skipped, error, timeout or pending results, against frozen 1,736-file
source in the pinned Linux runtime with Docker networking disabled. Its owned
container is removed. This includes actual HTTP publication, core composition,
manual delivery/recovery, tenant/Worker/lease/actor/metadata refusal, expiry,
rescheduling, completion, post-source rollback, and exact notification/audit
identity. Hosted delivery, 16 concurrent dispatch calls, original-job retry after
actual source failure/backoff, and preservation of other job types also pass.

Six canonical-store fixture transitions separately prove current recipient
account, Organization membership, Private Board grant, Organization deletion,
Board archive and List archive refusal without modifying the reminder first.
These are store-level transitions following actual HTTP reminder publication;
they do not prove each HTTP lifecycle orchestration. Five actual post-source
failures retain the original job and references, obey the original 30/60/120/240
second backoff, roll back all effects, and end in `FAILED` with five attempts,
version 11 and cleared lease. Advancing seven days cannot revive it.

Reports retained privately include the baseline, rollback (26), automatic (30)
and eligibility (37) scopes. Source manifests bind each compiled scope.

The earlier core-only complete API invocation has **718 passed and 8 failed
out of 726 actual unique results**. The new audit ownership guard exposed account
deactivation cleanup holding the Work gate without a per-Organization Work
scope. The repair marks only the Identity transaction that holds both gates as
owning Work cleanup, and cleanup establishes a scope for each Organization.
Existing deactivation tests additionally assert audit rollback and replay without
another fact; original assertions and deadlines remain. The repaired locked
solution build has zero warnings/errors. The repaired focused run passes all
**53 actual unique cases**, with zero bad counters, against a separately frozen
1,736-file source. All eight earlier complete-run failures individually pass
across this repaired scope and the separate two-case actual receipt-expiry run
(`demo-reminder-identity-audit-final-admission-native-20261010/api.trx`), whose
unique results and counters also match with no bad results. The
manifest hashes match every frozen file, and backend source/tests match the
compiled scope. Its owned container is removed. The repaired complete API run passes **740/740 actual unique execution IDs**,
with matching counters and no failed/skipped/error/timeout/pending outcomes, in
`demo-reminder-identity-audit-full-api-native-20261010/api.trx`. File/name
multiplicity comparison retains all 717 original cases and adds exactly 23.
All 1,736 frozen file hashes match; current compiled backend and API/domain test
source matches the frozen scope. Later frontend/browser/CI changes are verified
separately. The complete runner exits zero and removes its owned container.
An initial focused invocation
was rejected by the runner for an invalid wildcard filter before tests executed;
the corrected invocation retains the test scope.

The earlier hosted complete run is terminal with **725 passed and 8 failed out
of 733 unique cases**. Its eight failed identities match the earlier core-only
audit-boundary failures exactly, with no new failed identities. That pre-repair
run cannot certify the later Identity-boundary repair. Current immutable images, native UI acceptance,
verified-policy execution and all original PRD/architecture criteria remain
required. Estimated ARCH-03 work remaining stays **60%**; no ticket is closed
by this local proof.

Unit fixtures explicitly control dispatch through DI, following the existing
Demo deletion simulation pattern. The hosted case opts in to the real processor;
normal Demo composition enables it. Production registers no Demo processor.

## Real-clock native Demo acceptance and required CI wiring

Two new desktop/phone cases sign in through MUI using the documented seeded,
verified account. They publish an actual near-future due date and personal
AT_DUE choice, retain the real 60-second schedule, and open two empty private
native inboxes before delivery. They require the default Demo API host's FIRED
transition, one notification, original scheduling receipt replay, identical
creation/read envelopes across private frames and canonical HTTP journal,
keyboard cross-client read, tagged accessibility and overflow checks. No clock,
lease, source or notification is fabricated. The canonical in-memory audit and
atomicity assertions retain their separate complete API scope.

These cases are appended to `card-reminders.spec.ts` and registered only in
Demo mode. All four original Production case source bytes, assertions and budgets
are unchanged. Production shard verification passes; no Production case is
removed or skipped. The existing exact-image Demo foundation stage now invokes
both new cases against its already loaded API/web images, with a separate JSON
report. Its mandatory integration verifier requires that command and Demo mode;
no image rebuild, provider or new runtime process is introduced. Browser
TypeScript and integration/shard checks pass.

The first local native fixture reached normal Development API startup but its
health probe could not reach an edge published solely on Docker Desktop's
internal network; no browser cases executed. Its terminal owned API/web/network
were removed. The corrected fixture keeps the API on one internal network and
attaches only the web edge to a separate browser-facing network. It uses strict
email verification and no PostgreSQL, Worker or external provider. The same
health and case deadlines remain. That phase executes both cases but times out
before sign-in because the new fixture used an exact Password label rather than
the required-field selector used by the existing account tests. The new selector
is corrected to `getByLabel(/^Password/)`; original Production cases are unchanged.
A subsequent fixture health check also fails because its copied proxy references
the prior container name. These failed phases are retained privately and their
owned containers/networks are removed.

The corrected complete native invocation passes **2/2 unique source spec IDs on
first attempts**, with zero skipped/flaky/unexpected/report errors, in
184,730.463 ms. It uses the same 60-second real schedule and 180-second case budget.
The actual seeded account is verified under required-email policy. Both desktop
and phone cases prove actual automatic delivery, two native private inboxes,
original scheduling receipt replay, identical creation/read frames and canonical
HTTP journal, keyboard read reconciliation, tagged accessibility and overflow.
All three source-manifest hashes match. The runner exits zero; independent Docker
inspection confirms its API/web containers and networks are absent and the shared
PostgreSQL container remains. This is compiled-runtime local acceptance, not proof
that current retained images or the full release gate passed.

All **154 integration coverage regression checks** pass, including rejection of
omitting this Demo invocation or changing its runtime mode. Production browser
collection retains all 347 cases in 130 intact files across four shards.
