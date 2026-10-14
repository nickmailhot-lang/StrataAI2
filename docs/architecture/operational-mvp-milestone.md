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
