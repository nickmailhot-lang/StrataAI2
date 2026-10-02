# Kanban release evidence

[Run 36941858197](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36941858197)
at b87353a completed container job 110635994365 with failure. Decoded job logs
show 45 authenticated browser cases passed, one failed and one general-mail case
was skipped because the dedicated mail scenario ran separately (one passed).
The pipeline's required gate failed and release bundle was skipped; this is not
a green release.

Both list-position cases passed: desktop 1280px and phone 390px. At this commit,
they exercise keyboard review/confirmation, canonical order/revision checks and
reload; the desktop case additionally pointer-drags a list before its sibling
and checks persisted order/versions. This supplies actual runtime evidence for
that original list-drag path, rather than test collection alone.

The single failure is the phone relative-card-move unchanged-anchor comparison:
createdAt/updatedAt differ only in fractional timestamp precision between the
creation response and PostgreSQL read. ID, scope, rank, revision, title,
description and lifecycle match in the logged difference. Commit 617651f
subsequently repaired the baseline to use a persisted read; this old run lacks
that repair and does not establish repaired-case success.

This run predates target-based keyboard dragging, named announcements, canvas
card dragging, current-link focus restoration, bounded list scrolling and
restricted auto-scroll, outside-drop containment and new Kanban metrics. It
cannot verify those changes, complete assistive-technology/performance coverage,
or establish full PRD-06 acceptance. Current release runs still need their own
decoded logs and immutable artifact audit before any closure.
