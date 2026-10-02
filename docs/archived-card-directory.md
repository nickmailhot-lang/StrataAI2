# Archived card discovery (PRD-18)

`GET /boards/{id}/archived-cards?after={uuid}` requires current Board editing rights
and active Organization/Board scope. The command scope serializes with Board changes,
rechecks membership after lock waits, and verifies the live actor/session after the
read. Each request is a read: it creates no receipt, audit, event or job, even when
an Idempotency-Key header is supplied.

This matches Card archive/restore permission: contributors who can archive a Card
can find it again. Archived-List administration remains restricted to administrators.

Pages contain at most 50 archived cards, ordered by UUID with a strict seek cursor.
They include each parent List's current lifecycle and version. Archived Lists remain
visible here so users can understand the parent restoration prerequisite. Cards
under deleted Lists are excluded. Active/deleted cards never appear. The card
description is deliberately null in this directory response; this is a summary,
not a complete detail resource. PostgreSQL uses explicit tenant/Board predicates,
composite parent joins and forced RLS. Pagination is live rather than a frozen export.

Host regressions cover complete paging, summary privacy, parent archive/deletion,
invalid cursors and unauthorized callers. Required container checks extend the
existing archived-List fixture with restricted-role paging, unchanged product/
receipt state, current authority and session revocation during a Board lock wait.
The host regression passed on Linux at `2b8ecb1` in run `36991073544`; the added
restricted-role container checks and contributor permission refinement still need
runtime proof.

The Card archive page is reachable from active Boards with editing rights. It shows
bounded live pages with each parent List, explains archived-parent prerequisites,
and reviews the Card/parent before restoration. Card/parent revision changes block
old reviews. Lost or malformed acknowledgments retain the original version and key
outside the paged row, including after the committed Card disappears. Full fetch
and response-body deadlines are 15 seconds. Denied reads clear protected data and
abort/fence pending writes. Realtime invalidations queue behind an in-flight read
so repeated events cannot starve a slow current page. Dialog exit restores focus
after current discovery settles. Summary descriptions and server error titles are
not rendered.

Component checks cover parent changes, immutable recovery, scoped acknowledgments,
summary validation/privacy, bounded paging, hung response bodies, queued reads and
denial during a write. Desktop/phone release cases exercise keyboard review, live
parent changes, a lost committed restore, another client's updated canvas, unchanged
neighbor data, receipt recovery, focus and reload. Collection is not runtime proof.
Large-data latency, Card deletion consent, telemetry and retention acceptance still
need work; those remaining requirements keep PRD-18 open.
