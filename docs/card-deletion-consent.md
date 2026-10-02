# Permanent Card deletion (PRD-08 / PRD-18)

`DELETE /cards/{id}?version={reviewedVersion}&confirmed=true` requires an archived
Card, current administration, active Organization/Board scope and a nondeleted
parent List. Missing/false confirmation returns `delete_confirmation_required`
after authority/lifecycle checks. Confirmation is part of the deletion-only command
fingerprint; archive and restore fingerprints remain compatible.

Only deletion admission may resolve a Card tombstone. It uses the existing typed
transaction-local routing policy, then current Board/parent authorization before
returning the actor's matching committed receipt. A retry cannot mutate twice.
Changed consent/key fingerprints are rejected. Removed authority, archived Board
or deleted parent cannot disclose the historical receipt. Normal Card lookup
excludes tombstones in both PostgreSQL and the host test store.

Host tests cover consent/lifecycle rejection, identical recovery, changed intent,
irreversibility, outsider denial, Board archive/recovery and parent deletion.
Required restricted-role container checks prove consent rejection without effects,
audit-write rollback, receipt recovery without extra entity/audit/event/job writes
and fresh authority. Local compilation/syntax checks do not establish Linux runtime
acceptance. Card deletion UI with irreversible review is the next increment;
retention, search/notification exclusions and remaining PRD clauses stay open.
