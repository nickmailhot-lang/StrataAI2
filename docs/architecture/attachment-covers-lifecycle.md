# Attachment covers and lifecycle decisions

This implementation decision links [PRD-14](https://github.com/nickmailhot-lang/StrataAI2/issues/15),
[PRD-18](https://github.com/nickmailhot-lang/StrataAI2/issues/19),
[PRD-05](https://github.com/nickmailhot-lang/StrataAI2/issues/6) and
[PRD-24](https://github.com/nickmailhot-lang/StrataAI2/issues/25).
It extends their existing acceptance work; it does not establish completion.

## Covers

ATTACH-FR-008 selection requires current owning-Board edit authority, the current
Card and File revisions, same Organization/Card identity, an Active Clean
PNG/JPEG/WebP original and a committed immutable publication receipt for its
verified PNG derivative. Clean MIME metadata alone is a necessary eligibility
check, never proof of preview publication or permission. A same-tenant/Card
composite foreign key will preserve ownership when the Card moves between Boards.
Selection/removal, Card revision, current Board stream sequence, audit and
CARD_COVER_CHANGED event must commit together and support original-command replay.

The cover is visible wherever the Card is admitted. On a PUBLIC Board this means
an explicit derivative-only public cover projection. Current internal attachment
metadata, original downloads and private previews retain Organization membership
admission; Board visibility does not make those private objects public. Cover
selection UI must explain public visibility when applicable. Public delivery must
re-admit the current PUBLIC Board, visible Card and selected Active Clean source,
without exposing original filenames, digests or provider identities. Owner Portal
access remains a separate future projection. Existing full-byte integrity staging,
outside-transaction provider work, post-stage/current/final delivery fences and
private no-store headers remain mandatory even for publicly visible cover bytes.

## Attachment archive and deletion

ATTACH-FR-009 follows LIFE-FR-001/002/006/007/008: Active, Archived and Deleted are
explicit distinct states. Archive is reversible; permanent deletion requires an
Archived source, current elevated authority and explicit irreversibility consent.
Archive/restore require current edit authority and permitted active parent context.
The latest archive timestamp is retained after restoration; deletion retains the
archive timestamp, deletion time/actor and original immutable source identity.

Archive clears a selected cover atomically and excludes the attachment from normal
pages/cover eligibility. Restore never silently reselects the prior cover. A
separate authorized bounded archive review may admit archived originals and
verified previews; it does not restore ordinary disclosure or public cover access.
Scanning may safely finish an archived Pending file, but new preview production
and cover selection require an Active attachment. Deleted sources cannot be
restored, scanned or delivered. Permanent deletion clears the selected cover and
revokes all normal original/preview access atomically with revisions, audit and
ATTACHMENT_DELETED; archive/restore have their corresponding lifecycle events.

Private provider objects, immutable upload/preview evidence, tombstones and audit
remain retained until bounded provider reconciliation/retention work has explicit
authority to remove them. Product deletion is irreversible independently of backup
retention windows. No uncertain provider write is blindly deleted or overwritten.
Existing deleted fixtures are not assigned invented archive history during upgrade.

## Implementation order and acceptance evidence

First establish domain transition/precondition rules, then persist lifecycle and
cover ownership with forced RLS and restricted grants. Next implement current
authorized CAS/replay commands, archive pages and delivery fences, then MUI/native
cover and archive controls. Upgrade, restricted-role, current-authority withdrawal,
late transaction fence, two-client recovery, keyboard/mobile and exact-image
performance tests are required before closure. The existing preview/scan recovery
and release-image contracts continue to run. No new service or datastore is added.

The first implemented step is the Attachment domain model. Archive/restore are
idempotent, retain the latest archive timestamp and advance revisions only on
transitions. Restore rejects unavailable parent context and Deleted state. Delete
requires Archived state, a nonzero actor and explicit consent, records deletion
identity/time and prevents restoration/scanning. Failed timestamp validation leaves
state/history unchanged. Archived Pending scanning can finish without re-enabling
cover eligibility. Necessary cover-source checks require Active state; they still
do not grant permissions or prove a published derivative. These rules have domain
tests linked to PRD-14/18 TC-01/02/03/07/10. Persisted lifecycle metadata, authorized
commands, archive review and cover delivery/UI remain subsequent work.
