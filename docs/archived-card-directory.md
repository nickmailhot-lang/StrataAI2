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
runtime proof. This increment does not establish large-data
latency, archive UI, archived-card restoration UI, deletion consent, telemetry or
retention acceptance; those remaining requirements keep PRD-18 open.
