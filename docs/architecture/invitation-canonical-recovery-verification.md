# Canonical recovery after a lost invitation revocation acknowledgment

The retained `baad6b8c` invitation-administration trace records a real DELETE
with 204 in the request context and a lost browser acknowledgment. Later
authorized history reads return 200; a frame snapshot contains “Invitation
revocation confirmed.” before the failing Check revocation focus/enabled step.
Automatic canonical recovery has replaced that obsolete control.

The browser check now observes confirmed canonical recovery, or focuses and
activates the read-only Check action while still unresolved, inside one
five-second loop. It never repeats DELETE. The initial uncertain state and
absence of confirmation, explicit confirmation/cancellation, issuance reload
and original POST keys/counts, exact one-DELETE count, final row/role/revoked
timestamp and responsive-width assertions remain. The 90-second scenario
deadline and release limiter pacing are unchanged.

## Separate application error-state repair

A new quiet-recovery component regression exposes a genuine display defect:
validated recovery shows success but leaves the previous ambiguous revocation
error beside it. The baseline component file passes 49/50; the new stale-error
assertion fails. Canonical recovery now clears the old error only after the
current authorized row is validated. Missing rows, invalid/denied reads and
unconfirmed outcomes retain their existing handling. Final component coverage
passes 50/50, including no additional DELETE, lifecycle and authority guards.
The control assertion also waits for the normal dialog dismissal before finding
the newly admitted Refresh action. That wait does not remove a product check.

## Controlled reproduction and pending acceptance

A declared diagnostic sends one real foreground focus recovery after observing
the lost-response state, waits for a successful canonical invitation read and
the confirmed caption, then reaches the original Check step. Both desktop/mobile
fail there before repair: 0/2 passed in 105,416.330 ms, with zero skips/flaky cases.
No API reply or current-row data is mocked, and no DELETE is repeated. This is
supporting diagnostic evidence, not an ordinary unmodified acceptance run.

The repaired declared diagnostic passes both viewports in 92,939.938 ms,
without failures, skips or flaky cases. The complete ordinary repository
invocation, without injected focus recovery, passes both in 93,484.231 ms with
zero failures/skips/flaky cases. Both final captures independently have zero
mismatches across 2,086 hosted source/runtime hashes, 1,414 native source hashes
and four rebuilt web assets. All owned containers/database/roles and temporary
credential files are removed; original stopped PostgreSQL data is preserved. Web and browser type-checks, focused lint, production web build,
fresh zero-warning solution build and 213 immutable CI coverage guards pass.
Fixtures exclude only unused copied Windows native debug symbols; all tested
Linux runtime inputs, source hashes and reports remain intact.

Real restricted PostgreSQL/RLS, current API, separate Worker, durable issuance,
revocation and canonical history are exercised. Lost responses are the scenario's
explicit transport faults; managed mail delivery is not proven. Compiled runtimes
and rebuilt web assets are mounted in pinned runtime/edge containers, not exact
build-once candidate certification. Current complete immutable CI and all
remaining onboarding requirements still govern closure. Estimated PRD-60 work
remaining stays **18%**.

Related: [administrator workflows](invitation-administration-ui.md),
[history component and authority checks](../../apps/web/src/features/organizations/OrganizationInvitationHistoryPage.tsx),
[component regressions](../../apps/web/src/features/organizations/OrganizationInvitationHistoryPage.test.tsx),
and [complete Board browser scenarios](../../tests/browser/board-invitation-administration.spec.ts).
