# Recipient-private notification events (implementation in progress)

PRD-17 requires NOTIFICATION_CREATED and NOTIFICATION_READ separately from the
content-free Board replay stream. The Application envelope factory is implemented;
durable journal storage, authorized stream delivery and native recovery are not
implemented by this contract. Existing inbox HTTP polling remains its recovery
behavior until those integrations are verified.

The envelope identifies the stable notification, recipient, historical source
Organization/Board and actor. Creation uses the source actor, notification revision
one and original notification creation timestamp. Read uses the recipient actor,
revision two and the persisted first read timestamp. A retry clock cannot produce
a replacement read timestamp. Event identity and per-recipient sequence must be
assigned and retained by the journal transaction, never regenerated during stream
delivery or replay. Sequence values serialize as invariant decimal strings to
preserve bigint precision in the browser.

Metadata is an immutable empty dictionary. No Card title, description, comment,
profile, current Board projection or entity link is included. Consumers must
re-admit the inbox and obtain current authorized content/links from that service.
The envelope factory validates identities and timestamp ordering; it grants no
authorization and provides no durability by itself.

Next required integration: append creation with the originating notification
transaction and append the first read transition with its read transaction; retain
event identity on retry and suppress duplicate transitions. PostgreSQL storage
must preserve tenant isolation and recipient ownership without granting the Worker
general notification-table access. Reminder delivery through its narrow database
capability must produce the same atomic creation envelope. Delivery must check the
current session and Organization/source/current-entity visibility before and after
awaited reads, use bounded recipient pages and recover gaps without exposing other
recipients or trusting an old handshake principal. The demo adapter must implement
the same behavior while remaining explicitly non-durable. SignalR and the existing
API/Worker deployables remain the adopted transport/runtime architecture.

Three source tests cover identity/historical scope and absent projected Card data,
read actor/timestamp semantics and precise rejection of invalid scope/order.
Strict solution compilation succeeds with no warnings or errors. Local .NET test
execution is unavailable under Windows Application Control; Linux CI execution is
pending. No persisted event or completed PRD-17 acceptance is claimed.
