# Authentication and recovery support references

Sign-in, registration, password-reset request/consumption and email-verification
request/consumption now pair their fixed error wording with the validated
`X-Correlation-ID` from that response. They share the
[public reference contract](public-error-references.md): 1–64 ASCII letters,
digits, periods, underscores or hyphens. Missing or malformed metadata produces
no displayed reference. Provider/body diagnostics remain hidden.

Message and reference share one state value. Starting another attempt clears
the previous pair; local password-confirmation errors receive no server ID.
The response reference is captured before reading its JSON, so an unreadable
acknowledgment can still identify the actual received response. Network errors
without a received response get no fabricated reference. A response arriving
after cancellation cannot update the closed screen. Existing 15-second
deadlines, retry-key/body binding, confirmation rules, token withdrawal and
non-enumerating accepted notices remain unchanged.

## Executed verification

The two login/registration and four recovery reference regressions fail before
implementation: the selected baseline records 27 passes and six failures among
33 cases. After implementation, the initial complete authentication directory
passes 244/244 cases. Additional malformed-header/network, unreadable-body and
retry/late-response checks produce a final combined authentication and API
boundary result of **309/309 cases across 21 files**, zero failed or pending.
Original criteria and no-case-retry behavior remain. Typecheck, lint and a
current private Vite build pass. Reports/build output remain outside source in
`identity-error-references-20261010`. Initial setup commands used the wrong
working-directory/dependency path and did not run tests; they are not included
in either executed test count.

These are source and build results. Native browser execution is recorded
separately below; immutable-image CI is not inferred from source tests. Profile, invitation and other screen consumers still require
review; this does not establish all error-reference or authentication acceptance.
PRD-02 remains open at **16% estimated work remaining**; PRD-01 remains open at
**34%** (planning estimates).


## Current strict-verification browser phase

The original `identity-mail.spec.ts` desktop/phone pair passes **2/2 on their
only attempt**, with zero skipped, flaky, unexpected cases or report errors,
in 191,585.785 ms, on the
fb6f2fe0 frontend bundle, compiled schema-133 API/Worker and actual restricted
PostgreSQL, behind the existing Nginx/CSP boundary. Email verification is
required. The isolated provider accepts a delivery then loses its first
acknowledgment; the original cases require Worker retry without duplicate
effect, actual delivered tokens, consumed-token idempotency recovery after a
lost browser response, registration/sign-in and password reset. Original
120-second case deadlines, 45-second mail observation, command/body/key checks,
accessibility and document-width assertions are unchanged; no case retries.
Provider/account/token reports remain private. This is compiled native-runtime
evidence, not immutable-image release certification or every error-reference
display branch.

Private report: `identity-error-reference-browser-native-20261010`.

Owned API/web/Worker containers, fresh database and credential environments are
independently absent after terminal cleanup. The existing isolated provider and
shared PostgreSQL fixture are preserved.
