# Permanent List deletion consent (PRD-07 / PRD-18)

`DELETE /lists/{id}?version={reviewedVersion}&confirmed=true&containedCardCount={reviewedCount}`
requires current administration authority and an active Organization/Board, an
archived List, its current version, explicit confirmation and the current count
of contained active/archived cards. Deleted cards are excluded from this impact
count, exactly as in archive discovery. Missing confirmation/impact returns a
stable 400 code; changed impact returns `deletion_impact_changed` (409). Denied
callers receive the existing minimal unavailable response before protected
validation or counting. Failed consent changes no List/card/audit/event/job/receipt.

The Board gate serializes legal card movement and lifecycle commands while the
server checks impact and deletes the List. The List tombstone makes the parent
irreversible and excludes it from active/archived discovery and restoration.
Contained card records stay associated for historical integrity; they cannot be
edited, moved or restored through a deleted parent. Product deletion is not a
claim that retained database/backup bytes have been physically erased.

Confirmation and reviewed impact are part of the keyed deletion fingerprint.
Archive/restore fingerprints retain their existing format. A deletion-only typed
route can find the tombstone to authorize recovery of the same committed receipt,
without reapplying the mutation. Normal List lookup excludes tombstones. Replay
still requires current administration, active parent scope and the original
actor/key/fingerprint; changed consent, removed membership or parent archive
cannot recover protected historical data. No new tombstone disclosure endpoint
is exposed.

Host checks cover missing/negative/changed impact, unchanged archived data,
same-intent recovery, changed-key reuse, outsider and inactive-parent denial and
irreversibility. Required exact-image PostgreSQL checks also revoke audit writes
to prove rollback of claim/mutation/audit, then recover the same key without
additional product effects and reauthorize after grant/parent changes. Local
build and shell syntax pass. At `697459f`, Linux host tests and the required
archived-List/deletion container fixture passed in run `36988663443`. The full
pipeline and subsequent deletion UI browser checks remain pending.

The archive page now provides permanent deletion with the exact reviewed name and
card count, irreversible wording and a separate unchecked acknowledgment checkbox.
No DELETE can be submitted without that explicit consent. A changed count or List
revision blocks old consent; canceling/reopening a fresh review resets the checkbox.
Lost or malformed acknowledgments preserve the original version/count/confirmation/
key even after the deleted row disappears. The same dialog can recheck current
authority before recovering that receipt. Denied authority clears protected data.

Component regressions cover zero/nonzero impact, explicit confirmation, changed
impact without a List revision, unchanged historical retry, rejected impact and
wrong lifecycle acknowledgments. Two release-browser cases at desktop/phone widths
verify keyboard consent, deliberately lost committed deletion, two-client canonical
removal, identical retry, focus/reload, denied child restoration/movement and an
unchanged active neighbor. Runtime execution in Linux CI remains required; collected
cases do not prove browser acceptance. Archived-card browsing and remaining PRD-07/18
requirements stay open. Retention/purge policy is required under the wider tickets.
