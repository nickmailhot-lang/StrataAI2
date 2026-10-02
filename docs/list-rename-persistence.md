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

This advances List rename only. List copying, cross-Board movement and the other
remaining PRD-07 acceptance criteria still require implementation and evidence.
