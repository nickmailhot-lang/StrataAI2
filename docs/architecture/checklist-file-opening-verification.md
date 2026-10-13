# Checklist read and file chooser opening verification

Retained full-browser shard 2 from candidate `eea64913` fails the phone
checklist item read and desktop file chooser opening. Inspection of the retained
traces finds no item GET following the last failed child-opening action and no
upload-options GET following the file-opening action. Earlier successful reads
do not establish that these later openings succeeded. This does not prove the
exact cause of every hosted failure.

Both scenarios now use the existing five-second read-only opening helper and
observe their original target: canonical checklist progress, the incomplete
item text, or the file chooser. File enabled/focus checks remain. Focus and Enter
are admitted against the current visible, enabled launcher. Each helper attempt
uses bounded 500 ms control observations; when opening retires the launcher,
the helper observes the target instead of spending the entire outer deadline
waiting for the retired control. Mutation admission helpers are unchanged.

An initial changed four-case capture passed three cases. The remaining desktop
checklist failure retained a context showing the opened panel and exact expected
progress text. That exposed the retired-launcher helper timeout; this incomplete
capture is excluded from final acceptance evidence. The final complete original
repository invocation passes all four desktop/phone cases in 232,374.623 ms,
with zero failures, skips or flaky cases. The original 90/120-second scenario
deadlines, two-session live refresh, accessibility, upload/retry counts, original
keys and bytes, SHA checks, quarantine and captions are unchanged.

All 29 checklist and file-creation component checks pass, together with browser
type checking, a fresh solution build and 213 immutable CI guards. Independent
verification finds zero mismatches across 2,100 hosted source/runtime hashes,
1,414 native source hashes and four web assets. Read-only shared compiled builds
avoid repeated runtime copies. All owned containers, database/roles and temporary
credential files are removed; the original stopped PostgreSQL data is preserved.

The local fixture runs the real restricted PostgreSQL/RLS, API, separate Worker
and browser transport with optional API email verification and verified-account
Worker policy. File-provider responses remain explicit scenario simulations;
this does not establish S3 publication. Mounted compiled builds do not certify
the retained immutable CI images. Current complete build-once CI and the full
[checklist](checklist-acceptance.md) and [attachment](attachment-acceptance.md)
acceptance requirements remain necessary for closure. Estimated work remaining
stays **33% for PRD-13** and **32% for PRD-14**; both issues remain open.
