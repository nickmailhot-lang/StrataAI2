# Kanban release evidence

## Retained-source pointer fixture at f62785a6

[Run 37593384106](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37593384106)
completed container job `112707044278` with failure in the large-Board rank gate.
All preceding PostgreSQL rank, active/archived capacity, relative-move and durable
replay assertions passed. The phone capacity browser case passed; desktop failed
at the second vertical pointer activation after Escape cancellation.
Artifact `11471703060` retains the screenshot and trace. The first pointer lift
starts at `y=440.25`; the second starts at `y=-28.75`. Cancellation preserves the
focused/mounted source while auto-scroll places its handle outside the viewport.
Focusing that already-focused handle does not reveal it.

The fixture now explicitly reveals the handle before binding pointer geometry,
asserts that its center lies inside the visible Card surface, and requires a
native center hit test. Drag activation, traversal beyond the initial mounted
buffer, cancellation without writes, persisted movement and unchanged siblings
remain required. Browser TypeScript checks pass.

A separate local compiled-source/Vite run with actual Production registration,
restricted API storage and a scoped separate Worker used 200 Lists, 5,000 active
Cards and 100,000 archived Cards. Both desktop and phone timed out at the earlier
keyboard-move response wait, before reaching the changed pointer section. The
desktop trace records `net::ERR_ABORTED` for the move request and retained retry
UI. This is a separate unresolved local acknowledgment failure; it does not
confirm the pointer repair or establish full release/capacity acceptance. The
original CI run is terminal failure, not a live or green release. PRD-06 remains
open with its existing 35% estimated work remaining.

## Strict Mode drop submission and local follow-up

The local move trace records an abort about 184 ms after submission, before the
15-second request deadline. A new component regression reproduces the cause:
development Strict Mode replays mount effects and aborts the request that the
first effect started. Drop submission now runs in a microtask only if that
effect is still current. The replayed effect is retired before it can publish a
request, and unmount before publication prevents submission. Actual in-flight
unmount cancellation and uncertain-response same-key recovery remain intact.
The regression fails before the repair; all 21 move-control tests pass after
it, including the new no-publication-after-unmount case. Web type checking and
lint also pass.

Both local native capacity cases now complete the keyboard acknowledgment,
persisted order/sibling checks, vertical pointer Escape cancellation without
writes, and the subsequent real pointer drop with canonical placement/version
checks. This also supplies local native evidence for the preceding offscreen
source fixture correction. Both cases then fail at the later horizontal
destination observation (`destinationId` is null); that unresolved case and
complete current retained-image verification remain required. The database
still contains all 100,000 archived records with their original complete-row
fingerprint. No full capacity or performance acceptance is claimed. PRD-06
remains open with 34% estimated work remaining.

## Deferred-publication CI repair and horizontal diagnostics

[Run 37602053258](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37602053258)
at `ba3fb59a` failed web job `112728528028`: 133 test files passed and the
Board screen provisional-drop test failed because it asserted a synchronous
POST immediately after the drop. The command now intentionally publishes in a
microtask after mount replay retires. The test awaits that publication while
retaining its assertions inside the actual fetch callback that provisional
destination placement precedes persistence. Exact payload, single publication,
uncertain-result rollback and unchanged input snapshot remain required. The
Board screen test and all 21 move-control tests pass together locally.

The capacity fixture now retains bounded, content-free geometry in its failure
message: canvas scroll offset, visible viewport bounds, and mounted end-target
centers/bounds with their eligibility flags. The original five-second observer,
middle-half destination requirement and beyond-initial-buffer requirement remain
unchanged. A local desktop diagnostic reproduces the failure with `scrollLeft`
759: the first eligible destination center is 1361, outside the canvas's visible
280–1248 interval. Earlier mounted empty destinations are visible but correctly
ineligible because they belong to the initial buffer. This establishes that the
required later destination has not reached the observation area; it does not
establish the cause of stalled scrolling. An attempted change to choose direction
before scroll compensation still failed and was removed. Horizontal traversal,
complete capacity checks and current retained-image acceptance remain unfinished.
PRD-06 remains open with 34% estimated work remaining.

## Native capacity after stable window content

The unchanged desktop and phone capacity cases now both pass locally. They use
the actual Production API, restricted PostgreSQL role, schema 110, scoped separate
Worker and current Vite development source with the 200-List/5,000-active-Card/
100,000-archived-Card fixture. Desktop completes in 43.9 seconds and phone in
36.9 seconds. Between cases, the transferred fixture Card is restored through
the real version-checked move API; no Board response or live event is mocked.

Windowed rows now preserve unchanged content elements across scroll and sensor
frames. Previously those frames rebuilt every mounted List's nested controls.
The regression fails before the change, then proves content reuse and immediate
rendering of authoritative item changes. Native pointer direction is also tracked
from the initiating pointer's client coordinates, independently of dnd-kit's
scroll-adjusted public delta; keyboard direction remains on the existing path.
A separate regression proves large source/destination offsets cannot reverse
the chosen pointer axis. Pointer listeners retire on cancellation, completion,
replacement and Board screen unmount. Temporary diagnostic DOM attributes were
removed before the passing native runs.

All 44 focused windowing, auto-scroll, pointer-frame, Board-drop and move-control
tests pass, as do web/browser TypeScript and lint. Both native cases cover real
keyboard moves, vertical pointer cancellation without writes, persisted vertical
movement, horizontal movement to a newly mounted empty List, unchanged siblings,
Card detail focus/scroll recovery, keyboard List movement beyond the initial
buffer, bounded mounted rows and Axe WCAG checks. The archived count and original
complete-row fingerprint remain intact. These are compiled-source/local browser
checks, not a current immutable-image release or general performance claim.

The preceding `bb79aa21` web CI job `112733309819` in
[run 37603502855](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37603502855)
passes all 134 test files / 1,832 tests and production build. Its separate local
full-suite run had one application-shell failure; that shell's four tests pass
when rerun with the focused auto-scroll tests. The new window-content repair still
requires its own complete CI and retained-image acceptance. PRD-06 remains open
with 32% estimated work remaining.

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
