# Operational task-management MVP release milestone

The next release milestone is a deployable, stable operational task-management MVP. Complete its reliability and deployment acceptance before expanding governance and AI modules. The broader PRD roadmap and each issue's acceptance criteria remain in force; deferral does not constitute completion.

## Release scope

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

## Current status

The milestone is pending. The current source and container evidence must be distinguished from complete, successful CI and a retained release artifact. Continue core API reliability and current container/browser CI repairs first; do not treat an earlier commit or fixture-only provider evidence as a successful current release.

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
