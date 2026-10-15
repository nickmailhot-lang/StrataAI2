# List rename review and recovery (PRD-07)

An authorized active Board/List has an accessible rename action beside its List
heading. The MUI dialog reviews the name, rank and version, trims a 1–160 character
name and submits only name/version with a new request key. Renaming never sends
a position or changes sibling ranks or cards. The server remains authoritative.

A changed canonical name/rank/version preserves the local draft and disables
saving until the user explicitly discards it and reviews the current List. A
version conflict also requires this review. A denied response removes the draft
and action. Server error titles are never displayed.

An interrupted request, malformed acknowledgment or unavailable server retains
the original name, version and key. The bounded 15-second request can be retried
only as the same intent, including after newer canonical data arrives. Recovery
blocks dragging/repositioning that List while leaving its own retry available.
An acknowledgment must match entity, Organization, Board, name, unchanged rank,
active lifecycle and the exact next version. Success requests current data and
restores keyboard focus after the dialog exits.

Component checks cover scoped acknowledgments, every mismatched field, lost
responses, live draft conflicts, denied persistence, blank names, timeout and
unmount cancellation. Real release-browser cases at 1280px and 390px create a
Board with a card and neighbor, deliberately lose a successful rename response,
recover the unchanged keyed request, verify the other client's preserved dirty
draft, explicitly review another rename and verify persistence after reload.
They compare unchanged card/neighbor records and exact version increments.
Collection of these cases is not runtime proof; exact-image CI must execute them.

Run `36983420996` exposed a fixture admission error: its disposable Organization
was missing from the Worker's explicit delivery scope, leaving rename events
pending and preventing the other client's draft warning. The browser scenarios
now reuse `scopedBoardWorker` with cleanup, wait for durable initial delivery and
require connected streams before changing a List. Existing timing assertions are
unchanged. A fresh full run is required before claiming runtime acceptance.

This advances List rename only. [List copying](list-copy.md),
[cross-Board movement](list-board-movement.md) and the other PRD-07 requirements
retain their separate full acceptance criteria.

## Observe the rename editor before composing — 2026-10-15

Hosted run `38685376116`, job `116305340041`, passes 113 browser cases and
fails the phone List rename and live member-recovery cases. The rename trace
records a completed click on the other client's launcher, followed by waiting
for a textbox that never appears. The scenario file is unchanged between its
`ebfbfe1b` candidate and repair parent `dec7e2d1`. Artifact `11966523856` is
independently size/SHA-256 verified (23,822,595 bytes,
`9250bedd61df7e5d15f3ead96f5d02c0af75b84004b2276b6fd8dfe5060fa9f5`),
and its embedded metadata matches that candidate. The ZIP is read in memory;
only compact diagnostic evidence is retained.

The unchanged complete List/member desktop/phone baseline passes **4/4 in
232,345.808 ms**, one attempt each and zero unexpected, skipped, flaky or report
errors. This does not reproduce the hosted failure or establish a product root
cause. Both rename launchers now use the existing bounded five-second
`openAdmittedReview` helper and observe the actual name textbox before filling.
Opening is read-only; it cannot send a rename. Actual save/recovery activation,
two identical original-key/name/version PATCHes, lost committed response, dirty
peer draft, explicit discard, final version increments, unchanged Card/neighbor
graph, reload persistence, viewport checks and the 90-second deadline remain.

All **49** existing List/member component cases, browser TypeScript and **246**
CI coverage guards pass. Collection preserves **400** scenarios in **146**
intact files, partitioned **103/98/99/100**; collection is not execution.
The complete revised List/member desktop/phone files pass **4/4 in
228,599.683 ms**, one attempt each, with zero unexpected, skipped, flaky or report
errors. Both baseline and revised runs build the current frontend and mount it
read-only with current Nginx/header/default-listener settings over archive-verified
`24f56246` images. The API and separate Worker use real restricted PostgreSQL
with all 149 migrations. API account verification is optional, Worker policy
remains verified-account and managed mail delivery is disabled for the fixture.
This is a declared current-frontend/retained-backend fixture, not current-main
immutable-image or managed-provider certification.

All **173 browser inputs**, **367 frontend source inputs** and **four built web
files** match after execution. All three image identities stay unchanged;
owned cleanup restores the original Docker inventory and removes **3,329,665**
temporary bytes. The original database is unused and no images are built,
downloaded or imported. Current full immutable CI and the complete PRD acceptance
still govern closure. PRD-07 stays open at **17% estimated work remaining**;
large-load tests remain deferred until after the operational MVP.
