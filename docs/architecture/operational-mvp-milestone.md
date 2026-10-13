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

The List graph and event-publication capacity fixtures remain available behind `STRATAAI_POST_MVP_LOAD_TESTS=1`; keep that setting unset or `0` until the MVP milestone is achieved. Normal API reports explicitly mark the two 100,000-Card cases as deferred skips. The PostgreSQL List contract retains its three-Card lifecycle/rollback cases, and the event batch contract uses 20 references (49 events/jobs including retries and concurrency). Deferred cases do not count as completed PRD acceptance. Cancellation was requested for earlier CI runs containing the large fixtures; their cancellation is not a passing release result. Existing retained-image capacity stages still need explicit phase separation before the next CI push.

## Documentation links

- [Documentation index](../README.md)
- [Dependency order and complete roadmap](../ticket-dependencies.md)
- [Run the release bundle](../release/README.md)
- [Required production configuration](configuration.md)
- [Release integrity](release-artifact-integrity.md)
- [Source-free startup and restart evidence](release-bundle-startup.md)

## Current status

The milestone is pending. The current source and container evidence must be distinguished from complete, successful CI and a retained release artifact. Continue core API reliability and current container/browser CI repairs first; do not treat an earlier commit or fixture-only provider evidence as a successful current release.
