# Personal Board starring

PRD-04 BOARD-FR-005 uses the existing actor-scoped PUT/DELETE /boards/{id}/star writes and a new bounded GET of the same route. The private/no-store read returns only organizationId, boardId, userId and starred. It contains no Board content, membership directory or other users' preferences. Missing preference rows read as false.

The read requires an authenticated, current active account, an active Organization and current Board view admission. Its read transaction retains the Board/actor scope and verifies account admission before and after reading. Both demo and PostgreSQL resolve only the requested Board/current actor; PostgreSQL uses the existing tenant RLS transaction. Normal deleted-Board lookup denies disclosure.

Writes retain the existing actor/Organization/operation-scoped receipt contract. Replaying an earlier successful star request recovers its acknowledgment without overwriting a later unstar operation. Clients must read current preference state after acknowledgment and cannot infer that a replayed operation is still the latest preference. Other users' stars and shared Board/child revisions are independent.

API source coverage checks minimal fields, cache headers, two-user isolation, same-key replay after a later preference change, changed-operation conflict, unchanged Board/child records, anonymous denial and current membership revocation. Runtime proof depends on rigorous CI.

The personal star/unstar interface, account-bound retry/reconciliation, preference creation/revision metadata, realtime/event/audit/privacy policy, client telemetry and native/concurrent/performance acceptance remain unfinished. The existing writes do not yet emit the complete BOARD_STARRED producer contract; this read increment does not complete BOARD-FR-005 or PRD-04.

The source API mutation requests include the required X-StrataAI-Request intent header. Recent Board background/archive source fixtures were corrected to include it as well; runtime CSRF protection is unchanged. Full solution compilation passes without warnings/errors. This does not substitute for executed API/PostgreSQL acceptance.
