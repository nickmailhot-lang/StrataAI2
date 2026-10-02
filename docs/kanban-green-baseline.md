# Audited historical release baseline

[Run 36942494994](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36942494994)
at **9dcdff03d9dcb6821e2e3ad5972bce4865c2aad7** completed all nine CI jobs
successfully: web, .NET, PostgreSQL, source gate, build-once images, exact-image
integration, security, required-ci and release bundle. This is a historical
baseline, not verification of current main.

Decoded logs confirm 305 web tests in 29 files, 123 Domain tests and 178
API-host tests. Exact-image integration ran 46 authenticated browser cases
successfully; the general-mail case was skipped because its dedicated mobile
Worker-delivery scenario separately passed (one case). Desktop and phone list
positioning passed, including the desktop pointer drag at this revision.

PostgreSQL logs verify tenant-scoped durable jobs/lease fencing/retry/crash
checks, restricted runtime roles, invitation capabilities and canonical
Organization routing. Exact-image logs also confirm concurrent allocation and
append on a 5000-card fixture, concurrent rank-free card moves with historical
receipt recovery, relative list positions with non-reapplying replay, Board
invitation administration/consumer rollback and scoped Worker mail delivery.
These checks do not establish client rendering performance at capacity.

The container job verified all three image archive checksums before loading.
The release job downloaded the same immutable image artifact and copied its
web/API/Worker archives into bundle/images, rather than building new images.
Security ran dependency/secret/image checks and generated SBOM evidence; this
does not claim that every possible vulnerability is absent.

Artifact metadata was inspected after completion; all three match this exact
SHA and are non-expired:

| Artifact | ID | Bytes |
| --- | --- | ---: |
| strataai2-docker-bundle-9dcdff03d9dcb6821e2e3ad5972bce4865c2aad7 | 11201949413 | 237498759 |
| strataai-images-9dcdff03d9dcb6821e2e3ad5972bce4865c2aad7 | 11201385347 | 237461139 |
| security-evidence-9dcdff03d9dcb6821e2e3ad5972bce4865c2aad7 | 11200776220 | 35067 |

This run predates the deterministic expired-lease fixture, persisted anchor
baseline repair, target-based keyboard navigation/announcements, canvas card
dragging/focus restoration, bounded list scrolling/outside containment, Kanban
metrics and performance benchmark. The old timestamp-sensitive card comparison
happened to pass here; this does not validate the later repair or make that old
comparison reliable. Newer revisions require their own executed evidence.
No full PRD/architecture issue is complete from this historical green run.
