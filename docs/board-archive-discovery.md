# Board archive discovery

PRD-04 and PRD-18 use `GET /organizations/{id}/archived-boards?after={cursor}` as a bounded directory for lifecycle administration. The Organization must be active, and the current actor must have active Organization membership. Organization owners/admins see archived Boards in that Organization; other members see only archived Boards where their current Board role is Admin. Visibility alone does not grant archive administration. Active and deleted Boards are excluded.

The response contains organizationId, up to 50 items and a nullable nextCursor. Each item contains only id, organizationId, name, version and archivedAt. It excludes Board description, backgrounds, member directory and child content. Unknown historical archive clocks can remain null. The cursor is the last returned Board ID only when the 51st qualifying row establishes another page. Malformed/empty cursors fail after Organization admission. Responses are private/no-store.

The read unit of work validates current Organization/account admission before and after the read. PostgreSQL retains shared Board locks for the returned candidates through the owning transaction; canonical grant/lifecycle changes take the corresponding Board command gate. Demo reads use the existing transaction gate. Current grants filter before pagination.

Source API coverage checks 50/2 paging, minimal fields, archive clocks, active/deleted exclusion, Board administration/demotion, current membership, unauthenticated reads and invalid cursors. The exact-image PostgreSQL fixture checks paging, minimal fields, current admin grant/demotion and non-member denial. Full solution compilation and script syntax pass; runtime API/PostgreSQL evidence is pending rigorous CI.

The Organization screen links to a MUI archive directory with bounded seek paging and minimal Board summaries. Restore and permanent-delete dialogs review the discovered version; deletion requires explicit irreversible-impact consent. Unconfirmed changes retain the original command key, version and actor for acknowledgment recovery even after a committed deletion disappears from discovery. Conflicts require a fresh review.

Directory reads validate the current account before and after disclosure. Known account/admission withdrawal aborts a pending command and retires its review; a late acknowledgment cannot recreate it. Visible-only polling, foreground and online recovery coalesce into a bounded current read. Malformed scope, order, clocks, cursor or private extra fields withhold the directory. Dialogs announce outcomes and return focus after recovery.

Thirteen archive component cases and six Organization cases pass. These cover malformed disclosures, canonical restore, explicit deletion consent, same-key lost-response recovery, account change, permission withdrawal during a pending command and telemetry payload/recovery behavior. Web type checking, lint and production build pass; the combined archive observation/command suites have 27 passing tests. Full solution compilation passes without warnings or errors. API/PostgreSQL runtime proof remains pending rigorous CI.

Board archive observations use four fixed actions: archive_board_disclosure, archive_board_read, archive_board_restore and archive_board_delete. They report opens, use/retry/reconnect, success/failure duration, conflicts and exceptions through the existing bounded best-effort pipeline. Current-page polling is ordinary use; online/foreground recovery is reconnect. A reconnect queued behind another read keeps its classification. Client/server allowlists exclude scope IDs, names, clocks, versions, command keys and private diagnostics; parser source cases reject private extras atomically.

The Board screen now offers a reviewed archive control and its own fixed observation action; see board-archive-control.md. Native keyboard/mobile execution, Organization archive directory SignalR invalidation and large-data acceptance remain unfinished. Board directory observations do not establish lifecycle capacity.

## Reviewed account binding

Archive discovery, restore and permanent deletion support the optional
`X-StrataAI-Expected-Actor` header. When supplied, it must be exactly one nonempty
UUID matching the authenticated account. A mismatch returns neutral
`session_unavailable` before directory disclosure, command execution or receipt
lookup. Existing clients without this header retain the same authorization
requirements. The browser always sends the reviewed actor.

A restore/delete attempt has one bounded 15-second operation covering the
before-command profile, command response body and after-command profile. A
confirmed account change withdraws the private review and original intent. A
temporary failure before submission withdraws names and consent and states that
no Board change was sent; it does not create an unresolved command. Temporary
uncertainty after submission withholds the acknowledgment and retains only the
original command intent for explicit current-access verification and same-key
retry. A command transport failure keeps the existing canonical read/retry
recovery. A live invalidation during the before-command profile check withdraws
consent and fences the unsent command. Late replies cannot resurrect retired
reviews or receipts.

Validation: 23 archived Board component cases pass, including account switches
before/after submission, transient profile failures, noncooperative profile
requests bounded by the deadline, late replies and live review withdrawal before
submission. Two API cases pass for restore/deletion: wrong, malformed and empty
UUID actor headers disclose no directory and leave the archived revision
unchanged; the matching actor can execute and recover a byte-identical receipt
using the original key. Release compilation passes with zero warnings/errors.
Four native desktop/mobile cases in `board-archive-account.spec.ts` use normal
registration, authentication and persisted Board lifecycle endpoints. They
require zero commands/unchanged state before uncertain account admission and
exact-key recovery after a real committed command, keyboard use, focus return,
no document reload and Axe checks. These native cases remain pending exact-image
CI; local collection/typechecking alone does not prove runtime acceptance.

## Executed account uncertainty recovery

All four existing `board-archive-account.spec.ts` cases passed in one local
invocation against the Production API, restricted schema-110 PostgreSQL and
the production web bundle served through the current Nginx/CSP configuration.
Both restore and permanent deletion ran at 1280px and 390px. The tests verify
zero commands and unchanged archived state when the pre-command profile fails;
after a real committed command and failed profile confirmation, they verify
withheld success, fresh account admission and byte-identical command-key/body
recovery. Reviewed actor headers, explicit deletion consent, keyboard operation,
focus return, no document reload, Axe and horizontal overflow checks passed.

The invocation completed with four passes in 2.2 minutes, including intentional
rate-limit pacing; this duration is not a performance measurement. The fixture
uses the browser CI policy permitting unverified registered accounts. It does
not prove the strict verified-email policy, complete lifecycle matrix or
retained-image acceptance. API assemblies were mounted read-only from
the frozen `identity-policy-after-20261007` build; images were not rebuilt.

The three archive-specific scenarios in `organization-board-live.spec.ts` also
passed locally, together with its two ordinary-directory cases. Desktop/phone
archive clients received original Worker archive/restore events and recovered
an interrupted mirror. The phone Admin case proved private Board filtering,
role demotion/regrant resets, unchanged archived state after denied restore,
retired consent and membership withdrawal with a valid login. See
[executed live delivery](organization-board-realtime.md#executed-native-directory-delivery-and-withdrawal)
for the complete runtime scope. These passes supersede the earlier local native
execution gap, but retained-image CI and broader lifecycle acceptance remain.
