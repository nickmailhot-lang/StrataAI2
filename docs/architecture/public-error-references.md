# Public error correlation references

[Work request errors](../../apps/web/src/api/workManagement.ts) now apply the same
reference contract as the [API correlation middleware](../../src/StrataAI.Api/CorrelationIdMiddleware.cs):
1–64 ASCII letters/digits, hyphens, underscores or periods. Valid identifiers
retain their exact value. Missing, empty, oversized, Unicode, whitespace,
control-character, comma-separated or diagnostic-shaped values become null.
The browser does not generate a replacement that falsely implies a server log
correlation. Existing fixed public messages and admitted stable error codes remain.

This protects the existing Board/Organization error displays, which append the
returned reference to their fixed error text. Proxy response metadata must not
turn arbitrary diagnostic text into a public support reference. It does not
add references to every other screen or fabricate an HTTP request identifier
for a client-only/network failure.

## Executed scope

The new malformed-header regression fails against the old constructor: the
original selected request suite records **16 passed and 1 failed**. After the
initial boundary repair, the selected request, OrganizationHome and BoardScreen
component suites record **98/98 passed**, with no pending cases. A final added
constructed-error/control-character regression and whole-character validation
are independently verified by the complete final request suite: **18/18 passed**,
zero failures/pending cases. The final boundary report includes the later
control-character case; it is not silently added to the earlier combined count.
Typecheck and lint pass. Private reports: `public-error-reference-20261009`.

The initial CLI reporter used Vitest's default report location; that report was
moved to the private external evidence folder. Later invocations specify their
external report paths. An empty staging directory from an earlier incorrect
working-directory preparation remains because automatic approval review denied
its removal; it contains no files and is not part of the Git commit.

These are scoped source checks, not current immutable-image execution, complete
user-visible-error reference coverage, or full NFR/foundation acceptance. Current
CI, full error-consumer review and the remaining PRD requirements remain required.
PRD-01 stays open at **34% estimated work remaining** (planning estimate).
