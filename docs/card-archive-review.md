# Reviewed active Card archival (PRD-08 / PRD-18)

Active Card details expose Archive Card for current editors on active Boards with
an active parent List. The inline review names the Card/parent and explains reversible
removal from the canvas and restoration prerequisites. A changed Card or parent
revision invalidates an unsubmitted review. The command sends only reviewed version
and an idempotency key; acknowledgment must match Card, Organization, Board, List,
title, rank, archived lifecycle and the exact next version.

The detail route owns the control outside its conditional editor/canvas data.
Canonical disappearance after a lost response keeps the original version/key and
permits only that receipt recovery. Pending/uncertain archival blocks other Board
writes and detail closure while its own retry/current-read controls remain usable.
Acknowledgment closes details, rechecks the Board and returns focus to the remaining
Card link or Board refresh after current discovery. Editing revocation aborts/fences
the request and clears its reviewed intent; a late response cannot acknowledge or
resubmit it. Permission-denied Board reads unmount the scoped owner. Full fetch/body
deadlines remain 15 seconds, and server error titles/details are never rendered.

Component and Board regressions cover reviewed payload, removed-Card recovery,
scoped/malformed acknowledgment, parent changes, conflict, denied authority, hung
body and unmount. Desktop/phone archive-browser scenarios now use real UI archival
with a deliberately lost committed response before exercising reviewed restoration.
Collection is not runtime execution proof. Remaining full-ticket requirements,
including copy, cross-Board movement, watch, large-data behavior and telemetry,
still need implementation/evidence; this increment does not close PRD-08 or PRD-18.
