# Creation form admission during Board reads

The retained full-browser trace for `baad6b8c` records a tablet workflow failure:
the Add list activation completes, but the List name field never appears before
the original 30-second test deadline. Desktop and mobile pass in that hosted
invocation. The launcher is intentionally disabled while a protected Board read
checks access; activation does not establish that the creation form opened.

The complete workflow now confirms the List/Card creation field inside a
five-second read-only opening loop. It preserves desktop pointer clicks and
tablet/mobile focus plus Enter. Create/Save submissions, original Card retry
keys, exact request counts, persisted state, conflict drafts, history navigation,
tenant mismatch, public reader restrictions and viewport-fit assertions remain.
The test deadline and release limiter pacing are unchanged. The loop repeats
only an opening that has no server-side write effect.

## Controlled reproduction

A private fixture triggers the existing Refresh board action without moving
keyboard focus, holds the first real successful protected Board response during
the first Add list keypress, then forwards its unchanged content. Additional
reads proceed normally. If that real browser request is superseded, its actual
cancellation is recognized; a cancelled request cannot receive a response.
No replacement body, role grant, fabricated entity or repeated command is used.

Before repair, desktop passes and tablet/mobile both fail at the missing List
name field under the original 30-second deadline: 1/3 passed in 149,984.665 ms,
zero skips or flaky cases. Two earlier driver versions produced response-handler
errors; they are excluded from this clean three-viewport reproduction claim.
This declared diagnostic source override is supporting evidence, not an
unmodified repository acceptance run.

The repaired declared diagnostic passes all three viewports in 107,221.182 ms,
with zero failures, skips or flaky cases. The complete ordinary repository invocation passes all three viewports in
106,489.830 ms, without diagnostic injection, failures, skips or flaky cases.
Both final captures independently have zero mismatches across 2,084 hosted
source/runtime hashes, 1,414 native source hashes and four web assets. All owned
fixtures and temporary credential files are removed, preserving the original
stopped PostgreSQL container and volume. Browser type-checking, a zero-warning solution
build and all 213 immutable integration/browser coverage guards pass.

The fixtures use real restricted PostgreSQL/RLS, the API, a separate Worker,
strict verified-account fixture admission and browser transport. Only disposable
fixture users are activated by the existing helper; managed mail delivery is
not proven. Compiled runtimes/assets are mounted in pinned runtime/edge images,
so local results do not certify the build-once retained candidate. Current full
CI and the complete [PRD-01 acceptance map](prd-01-acceptance.md) govern closure.
Estimated PRD-01 work remaining stays **34%**.
