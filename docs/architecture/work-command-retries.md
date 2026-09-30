# Work Management retry contract

PRD-04..09 API retry requirements / PRD-24 revoked-access protection / ARCH-03
and ARCH-04 transaction and tenant boundaries.

Authenticated mutations under `/boards`, `/lists`, and `/cards` accept an optional
`Idempotency-Key` containing one nonempty UUID in standard hyphenated form.
Existing clients without a key retain the existing behavior. The board/list/card
creation and card edit UI sends a fresh key per intent and retains it when an
unchanged submission has an uncertain network or service outcome.
The recovery message asks the user to keep those fields unchanged and retry that
submission. Expired or reused keys have fixed recovery messages; raw API problem
titles, details, and unknown codes are never rendered.
Changed input or resource starts another intent; success clears the pending
intent. Keys are held in component memory, so navigation, closing the app, or a page reload does
not promise recovery of a pending key. Clients should inspect current state
before recreating an intent after losing that key.

Production stores the key in `work_command_replays`, scoped by Organization and
actor. A SHA-256 fingerprint includes the application operation, resource, and
all business input, including the expected version; it excludes the correlation
ID. A claim, successful typed result, domain mutation, and audit commit in the
same PostgreSQL transaction. Concurrent identical requests wait on the unique
claim and recover the same original result. Business failures and database
failures do not commit a claim. A failed commit acknowledgment still has an
uncertain outcome; retrying the same key resolves it if the original committed.
Cached results represent the original operation, not necessarily the entity's
latest version; consumers reload authorized current state after confirmation.

Reusing an actor/Organization key for different input, operation, or resource
returns `409 idempotency_key_reused`. A key is replayable for 24 hours using the
database clock. After that, `409 idempotency_key_expired` requires inspecting
current state before starting a new intent. Expired keys remain reserved; they
are never evicted and then accepted as fresh mutations. No cleanup handler is
implemented yet: encrypted storage/backup policy and a Worker job to redact
expired response bodies while retaining key/fingerprint tombstones remain
operational follow-up work. Do not indiscriminately delete retry rows.

Current authorization is checked before reading a result, after a duplicate wait,
and again against the saved result where needed. A created private board requires
current view access to that board even if its creator remains an Organization
member. Mutation replay requires the current operation's edit/admin/view rights;
revoked Organization or Board grants do not authorize replay. Deleted resources
or lost permissions may return the existing generic not-found response instead
of the saved result; retries still cannot repeat the mutation. This does not yet
provide locks against all concurrent parent lifecycle or permission changes.

The table has forced Organization RLS. Only the API role has SELECT/INSERT/UPDATE;
the Worker has no read access and the API has no DELETE grant. Response payloads
are typed operation results, not a primary domain store or event bus. Headers,
input bodies, fingerprints, and cached response bodies are not logged. Global
identity, organization creation, onboarding, other domain modules, event outbox,
and realtime replay require their own contracts and are not completed here.

Demo uses host-local results with the same key, fingerprint, expiration, and
authorization semantics, without PostgreSQL or integrations. Its results disappear
when the host restarts, and it refuses new keyed intents after 10,000 successes
rather than evicting an old key and permitting a duplicate. Production rollback
guarantees apply to PostgreSQL transactions; Demo is not a durable recovery store.

CI covers real API duplicates, simultaneous creates with a forced audit delay,
same key in another actor/Organization, changed input, versioned edits after an
API container restart, failed
audit rollback and key reuse afterward, forced RLS, restricted grants, expired
intent rejection, and retained Board-admin grants after Organization suspension.
The desktop and phone browser workflows deliberately lose a successful card
creation response, preserve the input/key, retry, and verify exactly one card.
