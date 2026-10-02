# Shared client Problem boundary (ARCH-02-FR-009)

Feature HTTP clients use `apiFetch`: same-origin paths, credential inclusion,
mutation CSRF protection and one normalized error boundary. Successful responses
and acknowledgment bodies are returned untouched. Error responses retain the
actual HTTP status, correlation/retry headers and deliberately approved stable
codes used by authentication, recovery, membership, invitation and work flows.

Error-body parsing is limited to 16 KiB and five seconds. Malformed, oversized,
unresponsive or unknown error payloads produce a fixed generic Problem body at
the original status. Original titles/details, stack traces, identities and
arbitrary extension fields are discarded. Cancellation is not allowed to wait
indefinitely for the error producer. Content length/encoding are removed from the
replacement response and its type is `application/problem+json`.

The code registry in `apiProblem.ts` is an explicit allowlist. Add a stable code
there when a feature needs it, with relevant correction/retry tests. Unknown
codes are omitted rather than used as a display message or analytics label.
Feature-specific labels remain fixed local text. Profile validation now uses
approved codes and its conflict status instead of a server-provided title;
draft preservation and explicit discard/reload behavior remain unchanged.

Regression coverage exercises preserved status/code/retry/correlation data,
unchanged successful acknowledgments, discarded private fields, unknown/proxy/
oversized bodies and stalled-body cancellation. Profile tests inject private
server titles while requiring safe field/conflict messages and retained drafts.
Existing invitation-correction and recovery-deadline tests remain required.
Typecheck/lint, the focused 66-test set and all 330 web tests across 34 files
passed. Exact-image verification remains required before this change is
considered release evidence.

ESLint rejects direct browser `fetch` references in production feature code,
including `window`, `globalThis`, `self` and request aliases. Only `apiFetch.ts`
and isolated test files are exempt. CI runs deliberate bypass fixtures against
the real ESLint configuration, requiring errors for those bypasses and allowing
the shared transport and normal feature imports. This protects the boundary as
new feature clients are added.
