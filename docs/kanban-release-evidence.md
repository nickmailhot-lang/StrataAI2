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


## Exact-image performance evidence at 5adf87f

[Run 37384373128](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37384373128)
retains browser-performance artifact **11383828920** for revision
`5adf87f6939cdff85704285a213ed537d15c1ccd`. Its ZIP SHA-256 was verified as
`1da11458a951537bb5192d60428904805e090de2d3cb80bad199ab8f810e5397`
before reading the measurements. Topology: exact release images through Nginx.

| Measurement | Desktop 1280×844 | Phone 390×844 | Budget |
| --- | --- | --- | --- |
| List drag feedback, two Lists and zero Cards | 43.3 ms | 36.1 ms, Chromium touch | 100 ms |
| Kanban usable, three Lists and 50 Cards, warm assets | 1013.1 ms | 1014.7 ms | 1500 ms |
| Card detail | 138.1 ms | 145.7 ms | 200 ms |
| Card drag feedback | 71.3 ms | 56.2 ms, Chromium touch | 100 ms |
| Mutation p95, 20 samples | 62.5 ms | 62.7 ms | 500 ms |

All listed measurements passed their recorded budgets. They prove these fixture
observations, not general device or network performance. The full authenticated
browser suite in this run recorded **143 passed, 13 failed, one skipped**;
required CI failed and no release bundle was produced. Subsequent browser
repairs on main require fresh native confirmation. This evidence does not close
PRD-06 or establish a green release.
