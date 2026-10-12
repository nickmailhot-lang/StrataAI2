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
