# Card assignment (PRD-11)

The assignment foundation exposes authenticated
`GET /boards/{boardId}/assignable-members?after=uuid`. It returns Organization
and Board IDs, up to 50 `{userId, displayName}` choices and a UUID seek cursor.
Names may be identical; stable user IDs identify choices. Email addresses,
account state, Organization roles and administrator profile fields are absent.
The existing administrator membership directory retains its separate contract.

Discovery requires current active Organization membership, view permission and
an active Board. An authenticated visitor to a PUBLIC Board has no membership
directory access. Admission precedes cursor validation. Private/unavailable
scope uses `board_not_found`; an admitted invalid cursor uses
`invalid_board_member_cursor`. Each returned user must have active explicit Board
membership, active Organization membership and an ACTIVE account, with verified
email when the configured identity policy requires it. Organization
administrators without explicit Board membership are not assignment candidates.

The PostgreSQL query applies eligibility before the 51-row limit using composite
tenant/Board joins. It uses the owning Board command transaction and existing
runtime privileges. The transaction rechecks admission after lock waits and
verifies the requesting session after the read. The demo provider follows the
same eligibility policy. The fixed `assignable_member_read` telemetry operation
uses existing stable result dimensions and excludes names or user IDs.

Host regressions cover scope, minimal disclosure, active membership/account
eligibility, Board removal, Organization removal, 50+2 pagination, invalid
cursors, anonymous/PUBLIC visitor denial and archived Boards. A required
exact-image fixture additionally reads through the release web proxy with the
restricted API database role and observes Board lock waits before membership
and session revocation. Strict local compilation and fixture syntax checks
passed. Host execution and exact PostgreSQL/runtime execution remain pending
Linux CI; Windows Application Control prevents local host-test execution.

PRD-11 remains open. Persisted multi-assignee associations and mutation APIs,
assignment cleanup on departures, Card face/detail UI, member filters,
historical attribution, notification suppression, two-client/accessibility
acceptance and documented performance evidence still require implementation.
Historical users and events must remain stable during membership cleanup.
