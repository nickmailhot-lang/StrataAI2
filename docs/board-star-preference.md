# Personal Board starring

PRD-04 BOARD-FR-005 uses the existing actor-scoped PUT/DELETE /boards/{id}/star writes and a new bounded GET of the same route. The private/no-store read returns only organizationId, boardId, userId, starred, createdAt, updatedAt and version. It contains no Board content, membership directory or other users' preferences. Missing preference rows read as false with revision 0 and null clocks.

The read requires an authenticated, current active account, an active Organization and current Board view admission. Its read transaction retains the Board/actor scope and verifies account admission before and after reading. Both demo and PostgreSQL resolve only the requested Board/current actor; PostgreSQL uses the existing tenant RLS transaction. Normal deleted-Board lookup denies disclosure.

Writes retain the existing actor/Organization/operation-scoped receipt contract. Replaying an earlier successful star request recovers its acknowledgment without overwriting a later unstar operation. Clients must read current preference state after acknowledgment and cannot infer that a replayed operation is still the latest preference. Other users' stars and shared Board/child revisions are independent.

PUT/DELETE require the reviewed personal revision in the version query parameter (0 for an absent preference). Missing, malformed or negative revisions are rejected with invalid_board_star_version after fresh Board view admission; a stale revision returns version_conflict without mutation. The exclusive Board command scope retains preference-read/write atomicity. The receipt fingerprint includes both desired state and reviewed revision: retries preserve the original query/key even after newer state is read, and reusing a key with another revision is rejected. The MUI control, demo fixture and restricted/browser release scenarios use this contract.

API source coverage checks minimal fields, cache headers, two-user isolation, same-key replay after a later preference change, changed-operation conflict, unchanged Board/child records, anonymous denial and current membership revocation. Runtime proof depends on rigorous CI.

The exact-release CI fixture also exercises these properties against the restricted PostgreSQL API runtime, including the same receipt key in two actor namespaces and rejection of a previously successful receipt after Organization membership suspension. It leaves the stored Board grant and preference intact during revocation so fresh admission must deny both disclosure and replay. Shared Board/child records are compared before and after personal changes. This fixture is collected sequentially by the existing build-once container integration job; executed proof remains pending until that job passes.

The MUI Board starring dialog reads only when opened and reconciles with visible polling, foreground and online admission. It checks an active account before and after each preference read, strictly validates the minimal Board/Organization/actor response, and checks the bound account before each write or receipt recovery. It retains the original operation and key after an unknown outcome, blocks replacement changes until recovery, then reads current preference instead of assuming a replayed acknowledgment describes the latest state. Account changes, fresh authorization denial, Board navigation and admission withdrawal retire old work; pending requests abort and late responses cannot disclose or replace current state. Fixed notices omit server diagnostics. Personal starring remains available to viewers, including read-only Boards where current server admission permits it.

Source component coverage exercises later-state reconciliation, retained operation/key after a lost response, account changes before initial submission and recovery, malformed/cross-scope reads, post-read identity changes, fresh denial, pending-write admission withdrawal, Board navigation and reconnect listener cleanup. Existing Board interaction tests remain passing.

The existing bounded activity telemetry endpoint accepts three fixed categories: board_star_disclosure, board_star_read and board_star_change. The control reports opening, reads, reconnect/retry, change use/retry, conflict/exception and bounded success/failure durations. Observations contain only action, kind, count and optional durationMs; no preference value, Organization/Board/account identifier, receipt key, path or diagnostic enters the payload. Client tests inspect real transmitted batches for lost-response recovery, reconnect and known 409/403 failures. Server parser tests reject entire batches with private extras. These untrusted observations are operational measurements, never audit history.

Migration 070 introduces retained preference creation clocks and positive revisions. New preferences have creation/update clocks and revision 1, including a first unstar. Unstar retains the row; subsequent value changes advance the revision and update clock while preserving creation. A repeated same-value command does not advance either field. Demo storage uses the same retained-row semantics and transaction rollback includes the preference dictionary. Historical creation clocks remain null because no original creation evidence exists; their revision 1 is an explicit first retained baseline. The migration leaves previous preference values/update clocks intact and is included in readiness admission. CI checks forward/repeat migration and restricted runtime creation/no-op/change/replay retention. The private read exposes these fields; clients validate positive retained revisions, a present update clock, ordered known creation clocks, and revision 0/null clocks for an absent preference. Historical null creation is explicitly admitted. Mutation acknowledgments remain 204, and consumers still read current state after acknowledgment. Both stores preserve the latest update clock if the system clock moves backward.

The restricted release fixture withdraws the API role's preference UPDATE capability, asserts a fixed storage-unavailable response without database details, compares the entire stored row including clocks/revision, and verifies that the tentative retry receipt rolled back. After restoring the capability, the original key commits once and its duplicate preserves the whole row. Fixture cleanup restores the capability even after assertion failure. This tests the actual transaction boundary; syntax validation alone does not prove PostgreSQL execution.

Migration 071 adds stable preference IDs and the separate actor-private board_star_events journal. Actual new preference transitions append BOARD_STARRED envelopes containing eventId, actorId, Organization/Board/preference identity, revision, an empty metadata object and timestamp. No private value/content enters shared Board events or activity. The trigger retains the preference transaction, validates immutable identity/creation and monotonic revision/clock transitions, and rejects journal mutation. API runtime gets journal SELECT only; direct event writes and preference deletion are withheld. Existing preferences receive IDs without fabricated events. Demo stores equivalent envelopes in a rollback-covered journal. Release fixtures check no-op/replay event counts, actor/preference consistency, private metadata, denied direct runtime journal writes, immutable history and rolled-back preference/event effects. Forward/repeat migration verifies historical values survive without invented events.

GET /boards/{id}/star/events?after={revision} reads the actor-private journal under the same fresh account/active Organization/Board view transaction as the preference read. It filters tenant, Board and actor, seeks by strictly increasing preference revision and returns at most 50 envelopes plus nextAfter when another page exists. The private/no-store page contains its Organization/Board/current-user scope. Authorization precedes cursor validation; revoked admission yields Board-not-found even for malformed cursors. Source API tests verify two-user isolation, immutable event identity, empty metadata, 50/3 paging, continuation, anonymous denial and revoked disclosure. Exact-image fixtures check actor filtering, continuation and revoked malformed-cursor denial. Execution is pending rigorous CI.

The authenticated SignalR endpoint /boards/live/stars streams Watch(boardId,cursor) actor-private pages through the existing /boards/live edge upgrade path. It binds the initial authenticated account, revalidates the live cookie and Board admission throughout delivery, and re-admits scope/account after selecting events. One subscription per connection limits outstanding work; cancellation releases it. An omitted cursor starts at the current private revision without replaying existing history. A retained nonnegative cursor replays bounded journal pages; a future cursor resets and replays available history. Initial/changed/reset frames and bounded heartbeats carry only scoped event envelopes. The cursor advances from the selection window, never from a newer post-selection head that could skip concurrent transitions. Exceptions use fixed errors/correlation-only logs; revocation aborts delivery. Source WebSocket tests cover account isolation, live transitions, cancellation/subscription limits, future reset/replay, Organization membership loss and logout. Runtime execution remains pending CI.

The MUI dialog consumes this private stream only after an admitted HTTP read binds the current account. Frames must have exact scope/envelope fields, valid clocks and revisions, empty metadata, ordered events and a valid bounded decimal cursor. Exact retained event replays are deduplicated by event identity and envelope fingerprint; altered replays and foreign actor/Board/Organization frames stop delivery before advancing the cursor. Initial handoff, reset, new events and reconnect invalidate the HTTP preference read rather than assuming an event describes the latest value. The stream supports transitions racing the initial selection, coalesces refreshes and preserves its actor binding across refreshes. Cleanup fences late callbacks, stops the connection and removes retry/refresh timers; account or admission withdrawal retires delivery. Existing bounded HTTP recovery remains available. Source tests cover strict validation, replay fingerprints, exact large cursors, reconnect and cleanup, and component refresh without subscription churn.

The dialog also offers an explicit private history disclosure. Each bounded page seeks by preference revision, validates the exact actor/Board/Organization envelope and increasing unique events, and checks the account before and after the protected read. Continuation is tied to the final revision and stable preference identity. The presentation uses a fixed personal-change label and account locale/timezone; it never invents a star/unstar value from the value-free event. Empty history explains that older changes may predate recording. Admission withdrawal hides/aborts old pages, account denial retires starring work, and scope/revision changes reset pagination. Keyboard controls provide page navigation, retry and close/focus return. Source tests verify minimal scoped pages, continuation, private disclosure, post-read account change and aborted late responses. Fixed starring telemetry categories cover disclosure/read/retry/results without event contents.

Native/concurrent/performance acceptance remains unfinished. This work does not complete BOARD-FR-005 or PRD-04.

The release browser suite includes 1280px and 390px scenarios using two independently authenticated accounts viewing an active PUBLIC Board. It intercepts a successful star acknowledgment, commits a later unstar and then drops the original response. Same-key receipt recovery must preserve that later state while the other account stars independently. The scenario checks authoritative preferences, unchanged shared records, unstar, keyboard operations, dialog focus return, reload persistence and viewport overflow. It reuses the immutable scoped Worker and stays within production sensitive-request budgets (six setup requests and five preference mutations). Browser type checking and collection pass; these scenarios have not yet executed against release images.

The source API mutation requests include the required X-StrataAI-Request intent header. Recent Board background/archive source fixtures were corrected to include it as well; runtime CSRF protection is unchanged. Full solution compilation passes without warnings/errors. This does not substitute for executed API/PostgreSQL acceptance.

Open history now recovers account preferences through the admitted identity
stream and visible-page ten-second, focus, online and visibility checks. The
same protected profile/page/profile read retains the current revision cursor;
signals during a bounded read queue one follow-up. Denial clears history and
retires recovery until fresh parent admission. Close/unmount removes listeners,
timers, stream subscription and the owned request. Dialog recovery preserves the
focused action and respects deliberate movement to another control.

All 33 combined activity/star/identity source cases pass, with web/browser
TypeScript and lint. The desktop/phone release fixture additionally changes the
account timezone through a normal versioned profile command from the other
client and requires the open mirror history to recover without reloading or
altering source timestamps/revisions. This adds one profile mutation to the
scenario budget. Its new native assertions remain pending exact-image CI;
PRD-02 and PRD-04 stay open.

Both complete desktop/phone star scenarios now pass against the local Production
API, restricted schema-110 PostgreSQL and current Vite source, with disposable
Workers scoped only to their new fixture Organizations and global discovery
disabled. Actual private WebSocket delivery, independent accounts, same-key retry
after a later unstar, immutable history timestamps, automatic timezone recovery
in the open mirror, unchanged Board/List state, closing focus, unstar and reload
all pass. Terminal exit 0, two cases in 58.1 seconds; timing is not a performance
benchmark. Workers retire afterward. These local builds are not the retained
release images; current full release and broader Board acceptance remain required.

The initial full run exposed a real closing-focus defect inside the MUI Dialog.
The activated Close button was removed before its ancestor could identify the
owned fallback. History now retains that exact Dialog reference before removal,
restores the opener and still respects deliberate external focus. An installed
Dialog regression fails before the fix and passes afterward. All 63 combined
star/activity/comment/identity component cases, web typechecking and lint pass.

## Initial keyboard admission in the current native run

At main `e375d2bb`, the four-case star/label baseline ended with three failures
and one pass in 3.7 minutes. Desktop starring failed its initial personal-state
assertion without issuing a star read; phone starring passed. The opener could
become temporarily disabled during live bootstrap after its initial enabled
check. The fixture now observes two successful, navigation-qualified protected
Board reads for each of its three clients before opening the personal dialog.
The existing observer rejects previous-screen, foreign, failed and mutation
responses; all four observer tests and browser typechecking pass.

Both unchanged substantive desktop/phone star workflows then pass together in
1.8 minutes against the same frozen compiled web/Production API, restricted
PostgreSQL and scoped Worker. Private delivery, account isolation, lost-response
same-key recovery after a later unstar, history/timezone recovery, immutable
timestamps/shared state and focus/reload checks remain intact. This changes
fixture readiness only. Current immutable CI and full Board acceptance remain
pending; estimated PRD-04 work remaining stays **16%** (planning estimate).
