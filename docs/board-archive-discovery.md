# Board archive discovery

PRD-04 and PRD-18 use `GET /organizations/{id}/archived-boards?after={cursor}` as a bounded directory for lifecycle administration. The Organization must be active, and the current actor must have active Organization membership. Organization owners/admins see archived Boards in that Organization; other members see only archived Boards where their current Board role is Admin. Visibility alone does not grant archive administration. Active and deleted Boards are excluded.

The response contains organizationId, up to 50 items and a nullable nextCursor. Each item contains only id, organizationId, name, version and archivedAt. It excludes Board description, backgrounds, member directory and child content. Unknown historical archive clocks can remain null. The cursor is the last returned Board ID only when the 51st qualifying row establishes another page. Malformed/empty cursors fail after Organization admission. Responses are private/no-store.

The read unit of work validates current Organization/account admission before and after the read. PostgreSQL retains shared Board locks for the returned candidates through the owning transaction; canonical grant/lifecycle changes take the corresponding Board command gate. Demo reads use the existing transaction gate. Current grants filter before pagination.

Source API coverage checks 50/2 paging, minimal fields, archive clocks, active/deleted exclusion, Board administration/demotion, current membership, unauthenticated reads and invalid cursors. The exact-image PostgreSQL fixture checks paging, minimal fields, current admin grant/demotion and non-member denial. Full solution compilation and script syntax pass; runtime API/PostgreSQL evidence is pending rigorous CI.

The MUI archive directory and lifecycle review controls remain unfinished. This API is a producer contract for those interfaces, not completed end-to-end archive acceptance.
