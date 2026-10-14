# Activity history admission during Board recovery

The hosted strict verified-account run for `baad6b8c` fails the mobile activity
scenario after account deactivation. Its retained artifact identity matches
that exact candidate. The trace records an older-page activity request with
HTTP 200, a later qualified Board recovery with HTTP 200, and subsequent
newest-page activity requests with HTTP 200. The duplicate positive attribution
assertion outside the paging recovery loop then sees zero matches.

The activity viewer deliberately hides protected history and retires its older
continuation when the parent access generation changes. The component regression
proves that an admitted older page is cleared during access checks, returns to
the newest page after fresh admission, and can reacquire the older immutable
author label through a fresh continuation. The security behavior remains intact.

The browser check now compares the original and renamed captions in one DOM
snapshot inside the existing five-second readmission loop. It still requires
exactly one original caption and zero renamed captions. Its initial positive
assertion, first-page-only paging guard, keyboard activation, 180-second overall
timeout, account deactivation, revoked session, reconnect and private-body
exclusion checks remain. Mutations are not repeated. A duplicate positive check
outside the protected generation was removed; completed admission does not
guarantee that a later access refresh will retain an older page.

## Verification boundaries

Before the repair, both complete ordinary desktop/mobile scenarios pass locally
without modification in 143,810.646 ms. This alone did not repair the hosted
failure. A diagnostic fixture introduces one real read-only “Refresh board” UI
action after initial historical attribution admission and waits for a qualified
Board response. No API reply is mocked. Both viewports then reproduce the same
outside-loop `toHaveCount(1)` failure, receiving zero, in 158,636.361 ms. With the
repaired checks, the same diagnostic invocation passes both in 140,107.369 ms,
without skips or flaky cases. This diagnostic source override is declared and
is not ordinary repository acceptance.

An earlier online-event diagnostic matched the mobile count failure but timed
out waiting for a qualified desktop Board read; it does not establish the same
cause on both viewports and is excluded from the paired reproduction claim.
The first fixture could not execute browser cases because the disk filled while
copying native files. Cleanup completed; unused copied Windows native debug
symbols were removed without changing source, reports or original database data.

The new component regression and complete activity-control file pass 17/17;
web and browser type-checks, focused lint, the 213 immutable integration/browser
coverage guards and a fresh zero-warning solution build pass. The ordinary
repaired full-file invocation passes both desktop/mobile cases in 135,922.028 ms,
with zero skips, failures or flaky cases and no diagnostic injection. Both final
captures have zero mismatches across 2,083 hosted source/runtime hashes, 1,414
native source hashes and four web assets, independently checked. All owned
containers, database/roles and temporary credential files are removed; the
original stopped PostgreSQL container and volume remain.

The fixtures use restricted PostgreSQL roles, forced tenant RLS, the real API,
separate Worker, strict verified-account fixture admission and actual browser
transport. Only newly registered disposable fixture users are activated by the
existing test helper; this does not prove managed email-provider delivery.
Compiled runtimes and web assets are mounted in pinned runtime/edge containers;
this is not an immutable-image release certification. Current complete CI and
the remaining [PRD-15 acceptance](prd-15-acceptance.md) still govern closure.

## First Board activity opening follow-up

Retained-image CI run 38488633147, notification job 115637762805, passes
41/42 cases in its strict Board-management invocation. Phone Activity history
fails at the initial Board review: the first-page assertion expects 50 rows and
receives zero. The scenario is unchanged between that candidate and `5d29125b`;
this failure is distinct from the later immutable-caption paging repair above.

Initial Board and Card openings now use the same bounded read-only admission
pattern already used by the peer. `openFirstActivityPage` observes the required
50 rows, choosing only the unopened review or the newest-page read if a
foreground generation withdraws admission. It checks enabled focus again before
Enter. The peer uses that shared helper as well. No mutation repeats, mocked
feed/authorization responses, relaxed row counts or wider deadlines are added.
The failure alone does not establish a new product root cause.

The complete maintained file passes both desktop/phone cases in
**173,724.01 ms**, zero failures/skips/flaky outcomes and no diagnostic source
hooks. Original 50/17-row pagination, focus, safe immutable captions, both
timezones and pushed preference change, actual two-client socket outage/recovery,
private-body exclusion, membership/session withdrawal, archive/delete/deactivation
commands, accessibility and 180-second scenario deadline remain. The 65 edits
are a small functional pagination fixture; no large-load testing runs.

Browser types, 222 CI guards and 398-case shard coverage pass. All 2,143 hosted
source/runtime hashes, 1,441 browser-source hashes, 168 frozen browser files and
four web files match. Owned containers, credentials, anonymous fixture volumes
and newly pulled support images are removed, preserving the original stopped
database. Actual restricted PostgreSQL, API, separate Worker and browser
transport use read-only mounted compiled applications and controlled account
setup. This is supporting interaction evidence; strict managed-mail/provider,
current retained-image/full CI and broader PRD acceptance remain required.
Estimated PRD-15 work remaining stays **34%**; the issue remains open.
