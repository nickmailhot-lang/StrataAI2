# Operational task-management MVP release milestone

The next release milestone is a deployable, stable operational task-management MVP. Complete its reliability and deployment acceptance before expanding governance and AI modules. The broader PRD roadmap and each issue's acceptance criteria remain in force; deferral does not constitute completion.

## Release scope

The [operator certificate lifetime check](https-deployment.md#operator-certificate-lifetime-check--2026-10-15)
adds a read-only command for monitoring the served endpoint's trust, identity
and usable lifetime. Its 25 TLS/input tests, 246 CI coverage guards and 37
artifact tests pass; a scoped native Nginx fixture passes three outcomes and
restores the original Docker inventory. Scheduling, tested alerts, renewal,
public deployment and full current-image release CI remain acceptance work.
No large load test runs; those remain deferred until after the MVP.

- Sign-in, account recovery, sessions, Organization membership and invitations.
- Boards, Lists and Cards, ordering, dates, labels, assignments, checklists, comments, archive and recovery.
- Authorized collaboration, activity, search and notifications, including reconnect and access withdrawal.
- Attachments with explicitly configured object storage and separate Worker processing.
- Documented Demo account, production configuration, migrations, restricted database roles, readiness, shutdown and restart.

Keep the adopted MUI SPA, ASP.NET modular monolith, separate Worker, PostgreSQL with forced tenant RLS and pgvector, and object storage. Preserve existing governance controls and tests while postponing new governance and AI expansion.

## Release acceptance

1. Resolve reproducible core workflow failures without retries that conceal failures, relaxed assertions, omitted tests or weaker performance budgets.
2. Pass all required CI jobs for the exact candidate commit. Reuse the three images built once by that run for integration, security and release assembly.
3. Verify the downloadable bundle's original image/security identity and checksums, and execute its source-free startup gate with migrations, restricted roles, health/build identity and graceful API/Worker restart.
4. Confirm the deployment configuration for public origin, durable keys, Worker Organization scopes, account verification/mail and enabled attachment providers. Fixture providers do not establish managed-provider readiness.
5. Verify core browser workflows, authorization withdrawal and recovery against the retained images. Mounted local builds and focused tests are supporting evidence, not release certification.
6. Document known limits, required operator settings and remaining acceptance gaps. Mark the milestone complete only with the exact passing run, candidate SHA and downloadable artifact; close individual tickets only on their own full acceptance criteria.

## Test sequencing

Large-load testing is deferred until **after** the operational MVP, as requested. Normal MVP runs use small functional fixtures for tenant isolation, authorization, lifecycle handling, atomic rollback, replay, event ordering, Worker delivery and deployment/restart checks. Production capacity and the broader PRD acceptance requirements remain unchanged.

The List graph and event-publication capacity fixtures remain available behind `STRATAAI_POST_MVP_LOAD_TESTS=1`; keep that setting unset or `0` until the MVP milestone is achieved. Normal API reports explicitly mark the two 100,000-Card cases as deferred skips. The PostgreSQL List contract retains its three-Card lifecycle/rollback cases, and the event batch contract uses 20 references (49 events/jobs including retries and concurrency). Organization-deletion candidate traversal uses 22 Cards, four Lists and four-item pages; its large deletion contract reports an explicit deferral. Deferred cases do not count as completed PRD acceptance. Cancellation was requested for earlier CI runs containing the large fixtures; their cancellation is not a passing release result.

Retained-image CI now gates **27 capacity producer/evidence stages** through the repository variable `STRATAAI_POST_MVP_LOAD_TESTS`. It is verified as `0`, and the workflow defaults to `0` when absent. The named stages remain registered in [the phase inventory](../../scripts/ci/post-mvp-load-steps.json): full-capacity Board copying, the combined capacity matrix, large-board rank/native-browser testing and their matching evidence stages. Only these stages may use the phase gate; functional checks cannot be deferred by it. All image identity, security, functional integration, Worker, browser, deployment/restart and required-CI gates remain active. Source fixtures receive the same flag. Enable it only after the MVP milestone, then run and inspect the deferred capacity acceptance before claiming the relevant full PRDs complete.

Verification of the split: 222 CI/startup guards pass, including refusal of missing phase gates, attempts to defer functional checks and hard-enabled source fixtures; workflow lint and a zero-warning/error solution build pass. Restricted PostgreSQL checks pass with the small datasets above, including concurrency, tenant isolation, late rollback, exact delivery references and actual Worker acknowledgment. The temporary fixture is removed with zero mismatches across 52 frozen source/runtime inputs. This is phase-policy and functional evidence, not a completed release milestone.

## Documentation links

- [Documentation index](../README.md)
- [Dependency order and complete roadmap](../ticket-dependencies.md)
- [Run the release bundle](../release/README.md)
- [Required production configuration](configuration.md)
- [Release integrity](release-artifact-integrity.md)
- [Source-free startup and restart evidence](release-bundle-startup.md)
- [Encrypted database recovery contract and remaining deployment requirements](database-recovery.md)

## Current status

The [fresh-cluster encrypted restore rehearsal](database-recovery.md#fresh-cluster-recovery-verification--2026-10-15)
now requires distinct PostgreSQL cluster identities and proves missing-role
transaction rollback before reviewed least-privilege role recreation. All 149
migrations and the existing two-Card fixture survive complete schema/data/ACL,
twelve refusal/corruption and restored tenant/security checks. Nine comparison
tests pass; 157 captured inputs match, and owned cleanup restores Docker inventory.
The final complete fixture takes 70.571 seconds. Full production runtime-role
recovery, deployed application/provider acceptance, scheduled drills and current
immutable CI remain required. No large-load test runs; ARCH-08 stays open.

The [durable attachment provider recovery checks](attachment-scanner-runtime.md#durable-provider-outage-recovery--2026-10-15)
pass two temporary-provider outage/recovery cases through actual restricted
PostgreSQL and the background processor. Quarantine remains closed during failure;
a fresh processor completes the same job with unchanged source identity and one
verdict publication. The bounded attachment contracts pass against 149 migrations
in 17.954 seconds, and owned cleanup restores Docker inventory. Providers/actor
admission are synthetic; deployed provider/engine recovery and current immutable
CI remain required. PRD-14 stays open at 32% estimated work remaining.

The [attachment readiness recovery acceptance](attachment-scanner-runtime.md#attachment-readiness-outages-and-recovery--2026-10-15)
passes six controlled HTTP/dependency/Unix-socket cases without changing product
logic. Sanitized readiness failure, independent liveness, provider deadlines,
caller cancellation and same-host/checker/socket-path recovery are verified.
The solution builds cleanly, frozen inputs match and owned cleanup restores
Docker inventory. Current immutable CI, real providers and durable upload/job
recovery remain required; PRD-14 stays open at 32% estimated work remaining.

The [attachment workload-identity repair](attachment-object-storage.md#workload-identity-runtime-dependency--2026-10-15)
adds the SDK STS module missing from the original runtime graph. Real SDK
credential exchange/signing and refusal tests pass after reproducing the missing
assembly; four existing transport cases also pass. API and Worker publish outputs
include the module with existing dependency versions unchanged. Owned fixtures
and publish outputs are removed. The release guide now supplies explicit
role/token/scanner setup. Current immutable images, real IAM/bucket/scanner and
complete enabled-provider acceptance remain required; PRD-14 stays open at 32%
estimated work remaining. No large load test is run.

The [auth rate-limit diagnostic follow-up](api-error-contract-verification.md#rate-limit-diagnostic-creation-and-writing-failures--2026-10-15)
preserves 429 and its retry hint when optional logger creation or writing fails.
Eight regressions fail before the repair, including two actual HTTP 500s; all
17 selected new/existing cases pass afterward. Quotas, authorization and response
contracts remain unchanged. The solution builds cleanly, frozen inputs match
and owned cleanup restores Docker inventory. Current immutable CI and deployed
acceptance remain required; PRD-02 stays open at 16% estimated work remaining.

The [List rename opening](../list-rename-persistence.md#observe-the-rename-editor-before-composing--2026-10-15)
and [live member-removal recovery admission](organization-member-administration.md#observe-original-removal-request-admission--2026-10-15)
follow-ups address two retained CI interaction failures without changing product
logic or original outcome assertions. The unchanged baseline passes, so no local
reproduction is claimed. Complete revised desktop/phone files pass 4/4 using a
current built frontend over verified retained backend images and fresh restricted
PostgreSQL. Inputs match and owned cleanup restores Docker inventory. All 49
existing component cases and 246 CI guards pass; all 400 browser cases remain
collected. Full current immutable release and managed-provider evidence remain
required. PRD-07 and PRD-03 stay open at 17% and 8% estimated work remaining.

The [API middleware diagnostics isolation](api-error-contract-verification.md#api-owned-middleware-diagnostics-isolation--2026-10-15)
preserves correlation scope, original downstream failures and intended
500/503/403 responses when optional log providers throw. Ten regressions fail
before the repair; all 22 selected middleware/HTTP/boundary cases pass afterward.
The solution builds cleanly, frozen inputs match and Docker inventory is restored.
This is scoped compiled-source evidence; current immutable CI, deployed logging
and full MVP acceptance remain pending. Large-load testing stays deferred.

The [configuration date-display follow-up](prd-02-acceptance.md#configuration-history-and-activity-date-preferences-2026-10-15)
repairs two existing administrator timestamp consumers to use final admitted
account preferences without changing source instants or adding requests. Four
regressions fail before/pass after; 109 selected component/transport/dependency
cases and frontend build checks pass. Native browser and complete current-image
CI remain pending; this adds no governance functionality.

The [Checklist acceptance audit](checklist-acceptance.md#executed-release-candidate-evidence-2026-10-15)
matches all 21 Checklist cases in ten unchanged files to passing job-log entries
at the completed `4cb80014` candidate below. It independently checks the small
retained performance artifact and all four original normal-condition budgets.
This replaces stale unexecuted-browser entries with actual evidence. Large
capacity, deployed telemetry and full current-main acceptance remain open; no
ticket is closed by the audit. Separate
[Checklist context cases](checklist-acceptance.md#checklist-specific-board-context-verification-2026-10-15)
now pass desktop/phone item editing with exact scroll restoration, Card focus,
canonical persistence and one document navigation against retained images.
Their current-main immutable CI remains required.

The [Worker diagnostics isolation follow-up](background-jobs.md#optional-outcome-diagnostics-isolation-2026-10-15)
preserves an already decided job outcome when optional outcome logging fails.
Eight regressions fail before the repair; all 21 processor cases pass afterward
in a scoped Linux runtime. It changes no retry, storage or handler behavior.
Full current immutable CI and broader durable queue/provider acceptance remain
required; large-load testing stays deferred.

The [outer Worker-loop follow-up](background-jobs.md#logging-failure-during-the-organization-loop-2026-10-15)
also isolates pass/warning logger failures so they cannot stop later scoped
Organizations after completed work or a claim error. Three actual loop lifecycle
cases fail before/pass after the repair, retaining exact execution counts,
fairness, safe errors and shutdown behavior. Current full immutable CI and
deployed logging acceptance remain pending.
The [shared Worker-owned logging boundary](background-jobs.md#shared-worker-owned-logging-boundary-2026-10-15)
extends this protection to identity delivery, invitation/discovery/maintenance
loops and startup scope disposal. Six new lifecycle cases fail before/pass after
the guard; the three Organization-loop cases still pass. Current complete
immutable CI and deployed-provider acceptance remain outstanding.

The latest completed CI release candidate is
`bb3cd4fc19a4edb3df035bc255a21973c772c54e`:
[run 38680420583](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38680420583)
is completed/success with all **16 jobs** successful, including build-once,
security, all seven integration executions, required-CI and release assembly.
The latest-successful-main workflow query independently identifies this run as
the newest passing CI candidate. Its
[tested Docker bundle](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38680420583/artifacts/11968632503)
is **518,727,662 bytes**; GitHub reports SHA-256
`289d8ae2767354f821ac81e73d9f2feef95f88a35860075988eb52e504b5bead`.

The small canonical metadata archive is independently size/SHA verified,
binding version **0.1.0-1752**, all three image references and this exact run/SHA.
The **95,630-byte** security archive is independently size/SHA verified, with
all six checksummed entries, four nonempty container SBOM inventories and the
three-image provenance record checked by the maintained verifier. This verifies
those records, not independently loaded image/archive correspondence. The full
bundle and image ZIPs have not been downloaded or executed locally for this run.

Inspected release logs confirm original image/security identity checks before
assembly, source-free migrations/restricted Production startup, exact runtime
identity, health/proxy, graceful restart and database-password rotation. The
source-free Demo gate passes exact three-image identity, no database/volumes,
SPA and seeded sign-in. Final unchanged-payload verification passes before
upload. Domain tests pass **1,033**; API tests pass **1,031**, with two explicit
capacity skips. All **27** capacity phases are observed and skipped; the repository
flag remains `0`.

The [encrypted PostgreSQL rehearsal](database-recovery.md#hosted-exact-commit-recovery-evidence--2026-10-15)
passes complete migrated schema/data/ACL comparison, five deliberate
corruption/refusal cases, pgvector, restricted tenant reads/writes, foreign keys
and append-only protections. Populated schema-149 upgrade checks also pass.
This candidate predates the operator backup/restore commands, optional HTTPS
listener, operator runtime/certificate checks, bounded readiness/outage repair
and later browser/diagnostics changes. Use
[this candidate's guide](https://github.com/nickmailhot-lang/StrataAI2/blob/bb3cd4fc19a4edb3df035bc255a21973c772c54e/docs/release/README.md).
Current-main immutable CI, actual deployment/providers, scheduled recovery and
the remaining MVP acceptance are still required. The milestone and incomplete
tickets remain open; large load testing remains deferred.

### Previous completed CI candidate — 4cb80014

The previous completed CI release candidate is
`4cb80014a9b9e863cdc68c9718e510886cade78b`:
[run 38674444706](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38674444706)
passes all **16 jobs**, including all seven integration executions, security,
required-CI and release assembly. Its
[tested Docker bundle](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38674444706/artifacts/11958117523)
is **518,723,401 bytes**; GitHub reports SHA-256
`c1dde5b5fe76afc5daf779cc3978a16cc66a9607ed8cf914f3090d8bb3b934f6`.
The small canonical metadata archive is independently read and size/SHA verified,
binding version `0.1.0-1748` and all three image references to this exact run/SHA.
The full bundle has not been independently downloaded or executed here.

The inspected hosted release job verifies original image/security identity and
checksums, assembles the source-free payload, passes migrations/restricted
Production health/build/proxy and graceful restart plus runtime-password
rotation, then passes the exact-image source-free Demo gate with no database or
volumes and the seeded sign-in. Final unchanged-payload verification passes
before upload. Domain tests pass **1,033**; API tests pass **1,031** with the two
capacity cases deferred. All **27** named capacity stages are observed and
skipped; the repository phase setting remains `0`.

This is historical passing-candidate evidence, not current-main or MVP certification. It
predates the encrypted recovery commands, optional HTTPS listener, build-bound
operator verification, bounded readiness/outage repair and later browser/upgrade
fixture changes. Those require their own complete immutable CI. Use the
[guide included with this candidate](https://github.com/nickmailhot-lang/StrataAI2/blob/4cb80014a9b9e863cdc68c9718e510886cade78b/docs/release/README.md)
and preserve the remaining deployment/provider acceptance below. The milestone
and incomplete tickets remain open.

### Previous completed CI candidate

An earlier completed CI release candidate is
`02bf121dae985412e75902b2c132b2338688a309`:
[run 38660360386](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38660360386)
passes all 16 jobs, including the build-once, security, seven integration,
required-CI and final release gates. Its
[tested Docker bundle](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38660360386/artifacts/11947266733)
is 518,718,398 bytes; GitHub reports SHA-256
`7b88eea946264b79056a6f93f86adfcb46144782cc926a7e5b4cfff45275d8fa`.
The completed release job verifies input identity/checksums, assembles the
payload, passes source-free migrations/restricted Production startup and
health/proxy/build identity plus graceful API/Worker restart, then re-verifies
the unchanged bundle before uploading it. These are inspected hosted CI and
artifact metadata results; this newer bundle has not been independently
downloaded and inspected on the local machine.

This candidate includes the external PostgreSQL TLS default and Production
HTTPS-origin validation. Domain tests pass 1,033/1,033; API tests pass 1,031 with
the two large-capacity cases deferred. All named capacity stages remain skipped,
and the repository load-test variable is reverified as `0`. It predates the
real PostgreSQL certificate contract, source-free Demo payload, database-secret
rotation rehearsal, later startup checks and browser admission/upgrade-fixture
repairs. Those changes require their own complete exact-commit CI. Follow the
instructions inside the selected bundle rather than assuming it contains
newer release assets. The milestone and architecture/feature tickets remain
open pending their remaining acceptance requirements.

The [database-outage follow-up](release-bundle-startup.md#database-outage-and-bounded-readiness-2026-10-15)
finds a readiness timeout against retained `24f56246` images. Current main adds
a bounded production readiness connection path and mandatory database stop/recover
verification. Focused Linux and compiled-library host checks pass; these repairs
still require their own complete current-image CI before MVP acceptance.

The [Production runtime admission follow-up](../release/promotion-and-rollback.md#production-runtime-admission-2026-10-15)
prevents healthy Demo/mixed-mode API and Worker hosts with the expected revision
from satisfying the operator's Production release check. Fourteen HTTP cases
fail before/pass after the repair. A real retained-image check confirms the
old command accepts all three wrong-mode combinations, the revised one refuses
them, and restored Production passes. Runtime inputs match and owned cleanup
restores Docker inventory. Full current immutable CI and deployment/provider
acceptance remain required; large-load tests remain post-MVP.

### Earlier independently inspected candidate

An earlier verified operational release candidate is available at
`24f562464f706884e4a27577b61c65ff385b4263`:
[run 38603745814](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38603745814)
passed every required source, PostgreSQL, build-once, security, integration,
browser, aggregate and final release gate. Its
[tested Docker bundle](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38603745814/artifacts/11925418731)
contains the exact three tested image archives and all 149 migrations.
Independent download verification matches the GitHub artifact digest, all
181 checksummed bundle files, original image/security identity and provenance,
and unchanged SBOM copies. The assembled source-free payload passed migrations,
restricted Production startup, health/proxy/build identity, graceful API/Worker
shutdown and restart in the mandatory release job.

Full-browser groups passed 103, 96, 115 and 82 cases. The second group's two
conditional mail cases passed in the dedicated 85-case notification group;
the foundation group passed 50 cases. All 27 registered capacity stages stayed
skipped, and the two 100,000-Card API cases stayed explicitly deferred. The six
retained normal-fixture performance measurements meet their unchanged budgets.
These counts do not establish production capacity or managed-provider readiness.

The milestone remains pending deployment acceptance. Confirm the operator's
public origin, durable key rings, Worker Organization scopes, verification/mail
transport and enabled attachment providers using the configuration and release
guides. Private storage/scanner and isolated mail fixtures do not prove deployed
AWS/IAM, antivirus or managed mail. Complete the remaining architecture and
feature acceptance audits before closing their tickets. Later main changes
require their own immutable CI; do not attribute either candidate's results
to a different revision. The newer candidate above covers the TLS-default/origin
repairs in its own scope.

The following entries preserve earlier investigation and supporting local
evidence in their original revision scope.

Small local browser evidence now includes the complete archived-List,
checklist-read and List-move files at desktop/phone: **6/6 in 318,145.017 ms**,
zero failures/skips/flaky outcomes, unchanged scenario deadlines, and no
diagnostic source hooks. Keyboard admission stops when the intended request is
observed; exact initial-write/original-key recovery counts and state assertions
remain. All captured source/runtime/browser/web inputs match, and owned
containers, credentials and newly pulled support images are removed. These
read-only mounted compiled applications supplement the current CI candidate;
they do not satisfy the exact-image release, full browser matrix or managed
provider gates above. Large-load testing remains disabled until this milestone
is actually achieved.

A separate complete Activity-history invocation passes desktop/phone **2/2 in
173,724.01 ms** after first-page read-only opening admission is aligned with the
existing peer pattern. Pagination, immutable captions, preferences, real socket
recovery, revocation, lifecycle commands, accessibility and deadlines remain.
Captured inputs match and owned fixtures are removed. This is a separate small
functional run, not an aggregate full browser or immutable release result.

Another separate complete filter-account/comment invocation passes both widths
**4/4 in 263,148.308 ms** after keyboard admission corrections. Original
account-withdrawal, receipt/count, unchanged graph, comment version/redaction,
preference, accessibility and deadline checks remain. Captured inputs match and
owned fixtures are removed. The complete current immutable-image pipeline and
all release gates remain pending; large-load testing stays disabled.

A separate complete checklist-collaboration invocation passes desktop/phone
**2/2 in 133,758.085 ms** after explicit discard is required to remove the old
editor before reopening. The strict canonical-value check, preserved dirty
draft/completion, conflict, actual socket recovery, committed receipt and revoked
replay, unchanged state, accessibility and deadline assertions remain. Captured
inputs match during execution and owned fixtures are removed. This supplements
the current candidate and does not certify the full immutable release pipeline.

A separate complete live-member/URL-attachment invocation passes desktop/phone
**4/4 in 213,675.344 ms** after the two explicit read-only member-review openings
observe the real review. Membership-event attribution, consent retirement,
original removal replay across restored membership, denied former access,
attachment receipt/count/URL safety, accessibility and deadlines remain intact.
All captured inputs independently match and owned fixtures are removed. The
unchanged URL cases passing locally do not prove repair of their separately
retained input-loss failure. Current complete immutable-image CI and the release
gates remain required; large-load testing stays disabled.

A separate complete ordinary URL attachment file now passes desktop/phone
**2/2 in 69,368.46 ms**, without diagnostic hooks, after focus-before-single-fill
and observed local-validation/command admission. A declared protected-read
overlap passes **2/2 in 69,453.412 ms** after the same change; its unchanged
baseline fails desktop local validation and passes phone. The exact retained
empty-URL failure remains for the current immutable pipeline to confirm.
Original receipt/count, persisted identity/version, safety, accessibility and
deadline checks remain. Captured inputs match and owned fixtures are removed.
The live repository capacity setting is rechecked as `0`; large-load testing
remains disabled, and full release acceptance is still pending.

A separate complete ordinary Board-member invocation passes desktop/phone
**2/2 in 95,045.416 ms**, without diagnostic hooks, after consent opening and
request observation share bounded admission. The retained desktop failure's
last unfinished action waited on a retired role dialog; activation now stops
when the intended request is observed. The competing 409, exact role/removal
counts and headers/keys, lost responses, fresh directory recovery, denied
recipient access, unchanged Organization membership, layout and original
90-second deadlines remain. Captured inputs independently match and owned
fixtures are removed. Current complete immutable-image CI and remaining
acceptance gates still govern release; large-load testing remains disabled.

Original file-upload recovery now survives an overlapping protected Board read
without abandoning its admitted replay. Five added regressions preserve
confirmed authority/account/scope withdrawal and first-upload cancellation;
all 24 component cases pass. The complete small hybrid provider pipeline and
attachment lifecycle files pass **2/2 and 18/18**, with zero retries/skips,
followed by the original HTTP privacy, ownership and lifecycle checks. All
2,143 captured inputs and four frontend assets match. Owned resources and
approximately 1.04 GB of image archives are removed; the original database is
preserved. [The verification record](attachment-object-storage.md#original-file-replay-during-a-protected-board-read-2026-10-14)
also records the full web run's one notification timeout and separate 25/25
rerun. This supports the repair; current complete immutable CI and remaining
release gates still govern MVP acceptance. The repository capacity setting is
rechecked as `0`; large-load testing remains deferred until after the milestone.

The complete selected-teammate desktop/phone comment file now passes **2/2 in
128,764.381 ms** after its two read-only openings observe the review/editor.
The retained hosted phone failure waits on the missing Add-comment control;
the unchanged local baseline also passes, so local reproduction is not claimed.
Original identity revision, receipt/count, private persisted notification,
revocation, accessibility and deadline checks remain. Captured inputs match,
owned fixtures are removed, and [the supporting evidence](comments-mentions-activity.md#selected-teammate-review-admission-follow-up)
keeps current immutable CI and full release acceptance pending. Large-load
testing remains disabled.

The [deployment identity follow-up](release-bundle-startup.md#image-identity-across-docker-stores-2026-10-14)
requires original-archive digest proof when Desktop and CI report different
configuration/manifest IDs. Matching labels cannot bypass identity. Integration,
security and source-free startup use this proof while keeping the original
provenance and checksums, and running containers must retain the verified local
IDs. Eighteen provenance tests, 25 artifact tests and 224 CI/shard/startup guards
pass; a real Desktop probe verifies three scratch images and three unstarted
container identities using only 2,424 bytes of archives, then removes them.
This identity probe is not production startup/restart or full immutable-release
acceptance. Those complete gates remain required, and large-load tests remain
disabled until after the MVP milestone.

## Additional navigation and deployment evidence (2026-10-14)

The [navigation replay repair](../navigation-observations.md#original-navigation-replay-across-protected-reads-2026-10-14)
keeps an admitted original request alive through protected reads without a
third dispatch, preserving scope/account withdrawal. All three unchanged
viewport scenarios and all 26 navigation checks pass; the full web suite passes
2,581 tests across 174 files. Original counts, receipts, session retirement,
later visits, Back, accessibility and deadlines remain. Captured inputs match
and owned fixtures are removed. Current complete immutable CI still governs
acceptance.

The maintained source-free deployment helper also exits 0 locally with retained
`5f399e35` images and the `48cb14ab` driver: migrations, restricted roles,
Production health/proxy/readiness, archive-bound image/build identity, graceful
API/Worker stop and restart of the same containers pass. Its outer private
audit exits 1 afterward on whole-machine network-ID equality; that is recorded
as a failed outer audit. Independent scoped checks prove the owned network's
create/destroy events and zero owned containers/volumes/networks, with images,
archives/tool, credentials and inspection removed. Original stopped PostgreSQL,
its volume and custom network are preserved. This supporting local execution
does not certify a physically clean host or new full release. Large-load tests
remain disabled; these samples close no ticket or milestone.

The [Board-edit focus repair](../board-metadata-control.md#owned-focus-after-protected-reads-2026-10-14)
preserves owned launcher focus through read admission, releasing it when the
user chooses another control. Retained phone evidence shows successful original
save/replay before focus fails; a corrected component case reproduces read-driven
focus loss. All 27 metadata/focus checks, 40 Board-screen checks and the complete
unchanged two-viewport browser file pass. Original save/receipt/revision, live
peer, draft, reload, focus and deadline checks remain. Captured inputs match and
owned fixtures are removed. These supporting results do not replace current
complete immutable CI or close PRD-04; large-load testing stays disabled.

The [Card move admission follow-up](../card-move-browser-evidence.md#observed-request-and-keyboard-pickup-admission-2026-10-14)
observes initial/recovery POSTs and actual keyboard pickup before proceeding.
Both unchanged and changed complete desktop/phone files pass locally; no hosted
failure reproduction or product root cause is claimed. The changed file passes
2/2 in 106,328.281 ms while preserving original requests/receipt, rank/version,
peer delivery, focus, zero-write cancellation, one committed keyboard drag,
pointer ordering, reload and deadlines. Captured inputs match and owned fixtures
are removed. Current immutable/full CI remains required; neither movement PRD
is closed, and large-load testing remains disabled.

The [cross-Board recovery follow-up](cross-board-card-movement.md#original-recovery-keyboard-admission-follow-up-2026-10-14)
observes initial/recovery POST admission. Retained phone evidence shows a
committed first request followed by a retry keypress without another POST;
the unchanged local baseline passes and is not claimed as a reproduction.
The complete changed desktop/phone file passes 2/2 in 109,064.009 ms, preserving
original body/key/count, source removal, destination placement, both peer
streams, two activity facts, focus/reload/overflow and deadlines. Inputs match
and owned fixtures are removed. Current immutable/full CI remains required;
PRD-08 stays open, and large-load testing remains disabled.

The [label-reference admission follow-up](label-keyboard-admission-verification.md#label-management-refusal-reference-opening-follow-up-2026-10-14)
observes management/editor opening and one intended PATCH. Retained phone
evidence has no browser label read after the opening keypress; the unchanged
local baseline passes, so reproduction is not claimed. The changed complete
read/edit desktop/phone file passes 4/4 in 179,620.583 ms with actual competing
edit, real refusal/reference, exact dispatch count, canonical unchanged labels,
reference retirement, accessibility, viewport and deadlines intact. Inputs match
and owned fixtures are removed. Current immutable/full CI remains required;
PRD-10 stays open, and large-load testing remains disabled.

The [checklist discard follow-up](checklist-review-admission-verification.md#observe-complete-review-retirement-2026-10-14)
requires the entire item-management review to retire before independent
reopening, since protected reads can temporarily hide its text box. The
unchanged local baseline passes; hosted reproduction is not claimed. The
complete changed desktop/phone file passes 2/2 in 123,530.732 ms with the
conflict, recovery, withdrawal, receipt and accessibility assertions intact.
Captured inputs match and owned fixtures are removed. PRD-13 stays open;
current immutable/full CI remains required, and large-load tests stay deferred
until after the operational MVP.

The [notification read recovery follow-up](notification-inbox.md#observe-original-read-and-recovery-admission-2026-10-14)
observes both intended POST admissions while preserving original key/body,
exactly two writes, one peer event, unread state and returned focus. The
retained hosted retry keypress sent no second browser POST; the unchanged
local baseline passes and is not claimed as reproduction. The complete
changed case passes 1/1 in 77,774.484 ms. Inputs match and owned fixtures are
removed. PRD-17 remains open pending current immutable/full CI and remaining
acceptance, with large-load tests deferred until after the operational MVP.

The [member group-mention admission follow-up](comments-mentions-activity.md#member-group-mention-review-admission-2026-10-14)
observes read-only review/editor opening before member text entry. The retained
phone trace stalls before a member comments read; the unchanged local baseline
passes and is not claimed as reproduction. The changed complete desktop/phone
file passes 2/2 in 149,827.496 ms with original receipt, explicit consent, role,
private delivery, actual rolling-quota refusal, withdrawal and accessibility
checks intact. Inputs match and owned fixtures are removed. PRD-15 remains
open pending current immutable/full CI and remaining acceptance. This small
functional fixture does not run the deferred large-load tests.

The [normal-board budget follow-up](../kanban-performance.md#operational-mvp-normal-board-budget-follow-up-2026-10-14)
confirms a current local desktop detail failure at 200.772 ms against the
unchanged <200 ms budget; phone passes at 178.978 ms. A diagnostic renderer
profile and discarded link prototype do not establish a verified fix. The
prototype's complete browser run still fails desktop feedback/detail and is
retained as failed evidence. Original source and bundle are restored, owned
fixtures are removed, and PRD-06 remains open at 25% estimated work remaining.
Normal responsiveness remains an MVP stability requirement; large-load
testing stays deferred until after that milestone.

The [painted detail readiness follow-up](../kanban-performance.md#native-click-painted-detail-readiness-2026-10-14)
measures the original native click through visible, focused, editable title
readiness after paint using the browser clock. It retains controller elapsed
time separately and labels measurement provenance without rewriting history.
The complete desktop/phone file passes 2/2 in 73,645.508 ms with all four normal
budgets unchanged; desktop paint/controller timings are 178.400/217.357 ms.
Missing observations remain failures. Inputs match, owned fixtures are removed
and original database data is preserved. PRD-06 stays open pending current
immutable/full CI and remaining acceptance; large-load tests stay deferred.

The [date/checklist timing alignment](../kanban-performance.md#datechecklist-timing-alignment-2026-10-14)
uses the same native-click painted-title observation and qualified read events,
retaining controller times and all original feature assertions/budgets. Both
unchanged baselines fail; the complete changed two-file run passes 2/2 in
105,395.105 ms. Date/checklist paint is 164.200/188.200 ms, controller time
234.303/268.537 ms, with normal readiness/mutation and checklist feedback
budgets passing. Inputs match and owned fixtures are removed. PRD-12/13 remain
open pending current immutable/full CI and full acceptance. Large-load tests
remain deferred until after the operational MVP.

The [return-to-Board activity follow-up](comments-mentions-activity.md#return-to-board-activity-review-admission-2026-10-14)
observes the actual first page before asserting the archived-Card caption.
Retained partition 1 passes 102 cases and fails this phone opening, with no
Board-activity GET after the keypress. The unchanged local baseline passes
and is not claimed as reproduction. The complete changed desktop/phone file
passes 2/2 in 148,553.389 ms, preserving archive/delete, immutable history,
recovery, privacy/revocation, paging and accessibility checks. Inputs match
and owned fixtures are removed. Current immutable/full CI remains required;
PRD-15 stays open and large-load tests stay deferred.

The [image-backed copy admission follow-up](../board-background-images.md#image-backed-copy-request-admission-2026-10-14)
observes one intended POST before awaiting the unchanged response and copied
image assertions. Retained phone evidence records a click without a POST; the
unchanged local baseline passes and is not claimed as reproduction. The changed
full fixture passes both copy viewports and all 18 lifecycle cases, plus the
independent ownership/privacy HTTP contracts. It uses verified once-built
661 images with current frontend assets and explicit private providers, not
current full-release/managed-provider certification. Inputs match; owned
containers, credentials, three images and about 1.04 GB of archives are removed
without runtime/symbol copies. Full current CI remains required, and large-load
tests stay deferred until after the milestone.

The [invitation receipt recovery follow-up](invitation-recipient-events.md#original-acceptance-event-before-explicit-receipt-recovery--2026-10-14)
orders explicit same-ID recovery after the original acceptance's live event and
protected discovery. The unchanged local file reproduced four missing-link
failures; the changed complete desktop/phone Internal/Portal/Board file passes
18/18. Stale-link withdrawal, account checks, exact receipts, authoritative
history and accessibility remain required. Captured inputs match and owned
fixtures are removed. PRD-60 remains open at 18% estimated work remaining;
current full immutable-image CI remains required and large-load tests stay
post-MVP.

The [retained-image startup follow-up](release-bundle-startup.md#retained-image-source-free-startup-follow-up--2026-10-14)
passes the unmodified source-free driver at `436ebe12` against retained `17bc37d7`
images: migrations, restricted roles, health/build identity and graceful
API/Worker restart. All 161 captured inputs match. The outer machine-wide
network inventory audit fails separately; its cause remains unestablished.
Independent checks find zero owned containers/volumes/networks and confirm
removal of three loaded images, downloaded archives and temporary credentials.
This supports deployment readiness but is not a passing current full release;
ARCH-11 and the milestone remain open, and large-load testing remains disabled.

The [Compose restart compatibility repair](release-bundle-startup.md#compose-v2-restart-compatibility--2026-10-14)
fixes the retained release job's rejected `start --wait` flag by using waited
`up` without dependencies or recreation. Original shutdown, container/image
identity, readiness and deadlines remain required. All 20 startup tests and 242
guards pass; the complete changed native driver passes against retained images.
Independent owned cleanup passes, with the separate built-in bridge-ID audit
failure documented. A complete new-commit immutable CI and release bundle are
still required. The milestone stays pending and large-load tests stay disabled.

The [checklist item-position peer opening follow-up](checklist-review-admission-verification.md#item-position-peer-read-admission--2026-10-14)
observes the first rendered item through the existing read-only admission helper
before asserting exact captions. Retained desktop evidence has no item GET after
the keypress; the unchanged local baseline passes and is not claimed as
reproduction. The complete changed desktop/phone file passes 2/2 with unchanged
move/replay/live-order/version/accessibility assertions and deadlines. Inputs
match and owned fixtures are removed. PRD-13 remains open at 33% estimated work
remaining; full current immutable CI remains required and large-load tests stay
deferred.

The [Board timezone clear admission follow-up](card-dates.md#board-timezone-clear-keyboard-admission--2026-10-14)
requires an enabled focused empty input before the existing one Save action,
and observes exactly one explicit-null/current-version PATCH. Retained evidence
shows UTC sent after the failed clear; the unchanged local baseline passes and
is not claimed as reproduction. The complete changed desktop/phone file passes
2/2 with original replay, persistence, live timezone/due-state and accessibility
assertions and deadlines. Inputs match and owned fixtures are removed. PRD-12
stays open at 24% estimated work remaining; full current immutable CI remains
required and large-load testing stays disabled until after the milestone.
