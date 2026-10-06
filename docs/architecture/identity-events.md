# Global identity events

PRD-02 and PRD-60 require account domain events in addition to audit records. Account state is global, while Organization membership and PortalAccess remain tenant-owned. A global account event therefore uses its actual user subject, with null Organization and Board identifiers. It must not publish membership, email, display name, avatar, password/token/session secrets or other content in metadata. The initial envelope permits only empty metadata and User entities; SESSION_REVOKED invalidates the subject's session state without disclosing a session identifier.

Migration 012 introduces a per-user sequence and content-free event log. Both tables force RLS against a transaction-local `app.identity_subject`. The API receives explicit stream/read/publication grants; it cannot update or delete event rows. The existing Organization Worker receives no identity-event grants, and the identity-mail capability is not repurposed. Future asynchronous processing requires its own reviewed capability. A subject GUC supplements server-side current-session checks; clients never choose a database subject context.

Publication must borrow the global identity command transaction, allocate sequence under the user stream lock and persist the authoritative entity version. Account state, audit and domain events must commit or roll back together. Registration has a newly created subject; verification proves its token subject; authenticated mutations prove the current user/session. No fabricated tenant session or nested transaction is allowed. Readers must prove the same current session before any event disclosure and after lock waits. Revoked or deactivated sessions receive denial rather than event history.

The envelope supports bounded ordered recovery with event-ID deduplication and an authoritative profile snapshot. It does not become an alternative source of profile truth or disclose other subjects. The browser validates contiguous sequences, matching subject/entity, empty metadata, null tenant/board IDs, bounded pages, positive versions and unique event IDs before advancing its cursor. It retains at most 1,000 deduplication IDs and follows continuation pages immediately, one bounded read at a time. Late reads after a mutation or unmount cannot advance the cursor or replace the profile. Dirty drafts remain preserved and newer authoritative versions drive conflict handling.

`GET /me/sync` derives the subject from authentication and enters the same fresh-session, user-lock boundary as an identity command. Without `after`, it returns the current profile and latest cursor with no historic events. With a nonnegative cursor, it returns at most 100 consecutive events, a next cursor, latest sequence and continuation flag. A future cursor is rejected. The profile and cursor are read while the subject lock is held, so committed profile changes cannot fall into a snapshot/cursor gap. A revoked session cannot retrieve its revocation event; current-session denial remains authoritative. Event-before-response reconciliation must respect profile versions.

Registration, profile update, deactivation, session revocation and verification append events after audit within the owning global command. PostgreSQL publication refuses an absent global identity root, borrows its transaction and allocates the sequence before inserting the current user version. Envelopes include correlation IDs; demo event creation uses the current clock.

Demo identity commands now snapshot the identity event list together with users,
sessions, recovery tokens and registered replay stores under the owning account
gate. A refused result, exception or cancellation restores those snapshots before
releasing the gate. Deactivation additionally holds the Work gate and restores
assignment and Work-event participants in the same failure boundary. Focused
rollback cases cover registration, sign-in, token consumption, recovery requests
and deactivation; newer API-host execution must be checked in CI. These snapshots
establish process-local rollback behavior, while durable PostgreSQL transaction
and event-publication acceptance requires the restricted database and exact-image
checks described below. The Demo audit sink does not supply durable audit proof.

Successful password reset also publishes one content-free SESSION_REVOKED event at the updated account version after revoking all existing sessions. The reset token proof, password change, token consumption, session revocations, PASSWORD_RESET_COMPLETED audit and event publication share the global transaction. Event insertion failure must roll everything back, including the stream sequence. Existing sessions receive authorization denial; only a fresh permitted session can replay the event. Reusing the single-use token does not publish another event or change the account. This publication does not provide durable command retry keys.

[Commit d1db46f CI](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36815213447) passed all nine jobs, including PostgreSQL verification/reset event-denial rollback, reset publication and repeated-token state invariance against the exact release images. The run retained the tested images, security evidence and release bundle for that revision.

Required CI fixtures cover actual runtime-role subject RLS, restricted Worker grants, event-publication denial rolling back profile/audit/sequence, replay after profile change, 100-event pagination, replay session revocation during a lock wait, and deactivation publication. Browser scenarios exercise automatic desktop/mobile profile recovery and logout denial through this endpoint. [Commit d51679c CI](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36811726894) passed these real PostgreSQL and exact-release-image checks. [Identity realtime delivery](identity-realtime.md) builds on this stream. The global stream does not complete invitation/suspension domain events, lifecycle ownership continuity or every PRD requirement. Durable identity command replay keys remain separate; storing raw credential/session/token responses in the existing Work replay table is prohibited.

The transactional identity service now revalidates the actor after assembling a
successful sync snapshot and before returning protected profile, events or cursor.
Initial admission and final admission share the owning identity transaction.
Two API-host cases use the real account/event stores and simulate actor withdrawal
at the final authorization check for both initial snapshot and event replay reads.
They require HTTP 401 with no protected profile/event/cursor disclosure, unchanged
account and event state, and successful identical recovery after admission returns.
This injected denial verifies boundary wiring; actual elapsed expiry and durable
release acceptance still require native CI evidence. Strict compilation is checked
separately from native test execution.

The mandatory exact-image identity command fixture also blocks the actual event
stream read using CI-only table locks after initial account/session admission.
For both initial sync and `after=0` replay, it observes the API runtime waiting on
the event-stream query, holds the read beyond the original session expiry, then
requires HTTP 401 with no profile/event/cursor disclosure or cookie. Complete
account/session/audit/stream/event/receipt state must remain unchanged. Restoring
the fixture session lifetime must recover the canonical profile and latest cursor.
Bash syntax passes; native execution remains pending. These disposable locks do
not change production grants, schema, isolation policy or runtime behavior.
